"""Grading engines for the GradeLens AI service.

Two implementations behind the same interface:

- HeuristicGrader: fully offline. Scores each criterion by keyword coverage
  and compares the answer to the exemplar with TF cosine similarity.
  Confidence is the agreement between those two independent signals.
- ClaudeGrader: calls the Claude API with a rubric-constrained tool schema,
  samples N times, and derives confidence from score spread (self-consistency)
  blended with the heuristic similarity signal.

The service picks ClaudeGrader when ANTHROPIC_API_KEY is set, otherwise
HeuristicGrader — so the whole system runs end-to-end without any API key.
"""

import json
import math
import os
import re
from collections import Counter

STOPWORDS = frozenset(
    """a an the and or of to in on for with is are was were be been it its this
    that these those as by from at each which such not no only into their there
    has have had will would can could should about between within""".split()
)

PROMPT_VERSION = "grading_v1"


def tokenize(text):
    return [t for t in re.findall(r"[a-z0-9']+", text.lower()) if t not in STOPWORDS]


def tf_cosine(text_a, text_b):
    a, b = Counter(tokenize(text_a)), Counter(tokenize(text_b))
    if not a or not b:
        return 0.0
    dot = sum(a[t] * b[t] for t in a)
    norm = math.sqrt(sum(v * v for v in a.values())) * math.sqrt(sum(v * v for v in b.values()))
    return dot / norm if norm else 0.0


class HeuristicGrader:
    """Deterministic offline grader. Not a real assessor — it exists so the
    pipeline, routing, and evals run without an API key, and it doubles as
    the independent signal the Claude grader is checked against.

    v2: quality = TF cosine to the exemplar blended with coverage of the
    exemplar's technical vocabulary (tokens >= 6 chars). Weights and scales
    were fit on eval/gold_dataset.json (v1 MAE 7.62 -> v2 MAE ~2.4/20);
    see docs/evals.md.
    """

    model_name = "heuristic-v2"

    SIM_WEIGHT, SIM_SCALE = 0.4, 0.6
    COV_WEIGHT, COV_SCALE = 0.7, 0.4

    def grade(self, request):
        answer_tokens = set(tokenize(request.answer_text))
        similarity = tf_cosine(request.answer_text, request.exemplar_answer)
        concepts = {t for t in tokenize(request.exemplar_answer) if len(t) >= 6}
        concept_cov = len(concepts & answer_tokens) / len(concepts) if concepts else 0.0

        quality = min(1.0, self.SIM_WEIGHT * similarity / self.SIM_SCALE
                      + self.COV_WEIGHT * concept_cov / self.COV_SCALE)

        scores = []
        for criterion in request.criteria:
            keywords = set(tokenize(criterion.description))
            matched = keywords & answer_tokens
            crit_cov = len(matched) / len(keywords) if keywords else 0.0

            fraction = min(1.0, 0.85 * quality + 0.15 * crit_cov)
            points = round(fraction * criterion.max_points)
            missing = sorted(keywords - matched)
            justification = (
                f"Overall quality {quality:.0%} (similarity {similarity:.0%}, "
                f"concept coverage {concept_cov:.0%}); criterion keywords {crit_cov:.0%}."
                + (f" Not addressed: {', '.join(missing[:5])}." if missing else "")
            )
            scores.append({"criterion_id": criterion.id, "points": points, "justification": justification})

        # Confidence = agreement between the two independent signals, not answer
        # quality: a confidently-graded weak answer publishes with low marks.
        sim_n = min(1.0, similarity / self.SIM_SCALE)
        cov_n = min(1.0, concept_cov / self.COV_SCALE)
        confidence = round(max(0.0, 1.0 - abs(sim_n - cov_n)), 2)

        weakest = sorted(zip(request.criteria, scores), key=lambda p: p[1]["points"] / max(p[0].max_points, 1))
        feedback = "Automated (offline) assessment. Strongest area: {}. Focus next on: {}.".format(
            weakest[-1][0].description.lower(), weakest[0][0].description.lower()
        )

        raw = json.dumps({"grader": self.model_name, "similarity": similarity, "concept_coverage": concept_cov})
        return scores, confidence, feedback, raw


class ClaudeGrader:
    """Rubric-constrained LLM grader with self-consistency confidence."""

    model_name = "claude-sonnet-5"
    samples = 3

    def __init__(self):
        import anthropic  # imported lazily so offline mode needs no key

        self.client = anthropic.Anthropic()

    def _tool_schema(self, request):
        return {
            "name": "submit_grade",
            "description": "Submit the grade for the student answer.",
            "input_schema": {
                "type": "object",
                "properties": {
                    "scores": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "criterion_id": {"type": "string", "enum": [c.id for c in request.criteria]},
                                "points": {"type": "integer", "minimum": 0},
                                "justification": {
                                    "type": "string",
                                    "description": "Must quote evidence from the student's answer.",
                                },
                            },
                            "required": ["criterion_id", "points", "justification"],
                        },
                    },
                    "feedback_text": {"type": "string", "description": "2-4 sentences of constructive feedback."},
                },
                "required": ["scores", "feedback_text"],
            },
        }

    def _prompt(self, request):
        criteria_text = "\n".join(
            f"- id={c.id} (max {c.max_points} pts): {c.description}" for c in request.criteria
        )
        return (
            "You are grading a student's free-text answer against a rubric.\n\n"
            f"QUESTION:\n{request.question_text}\n\n"
            f"EXEMPLAR ANSWER (full marks):\n{request.exemplar_answer}\n\n"
            f"RUBRIC CRITERIA:\n{criteria_text}\n\n"
            f"STUDENT ANSWER:\n{request.answer_text}\n\n"
            "Score every criterion. Each justification must cite specific evidence "
            "(or its absence) from the student answer. Award 0 for criteria the answer "
            "does not address. Use the submit_grade tool."
        )

    def _sample_once(self, request):
        response = self.client.messages.create(
            model=self.model_name,
            max_tokens=2000,
            temperature=0.5,
            tools=[self._tool_schema(request)],
            tool_choice={"type": "tool", "name": "submit_grade"},
            messages=[{"role": "user", "content": self._prompt(request)}],
        )
        payload = next(b.input for b in response.content if b.type == "tool_use")
        self._validate(payload, request)
        return payload

    @staticmethod
    def _validate(payload, request):
        max_by_id = {c.id: c.max_points for c in request.criteria}
        seen = {s["criterion_id"] for s in payload["scores"]}
        if seen != set(max_by_id):
            raise ValueError(f"scores must cover every criterion exactly once, got {seen}")
        for s in payload["scores"]:
            if not 0 <= s["points"] <= max_by_id[s["criterion_id"]]:
                raise ValueError(f"points {s['points']} out of range for criterion {s['criterion_id']}")

    def grade(self, request):
        samples = []
        for _ in range(self.samples):
            for attempt in range(2):  # one retry on schema violations
                try:
                    samples.append(self._sample_once(request))
                    break
                except ValueError:
                    if attempt == 1:
                        raise

        # Median score per criterion across samples; spread drives confidence.
        by_criterion = {c.id: sorted(s["points"] for smp in samples for s in smp["scores"] if s["criterion_id"] == c.id)
                        for c in request.criteria}
        final = samples[0]
        spreads = []
        for s in final["scores"]:
            pts = by_criterion[s["criterion_id"]]
            s["points"] = pts[len(pts) // 2]
            max_pts = next(c.max_points for c in request.criteria if c.id == s["criterion_id"])
            spreads.append((pts[-1] - pts[0]) / max(max_pts, 1))

        consistency = 1.0 - (sum(spreads) / len(spreads) if spreads else 0.0)

        # Independent-signal check: does the LLM's overall grade agree with
        # lexical similarity to the exemplar? Large disagreement lowers confidence.
        total = sum(s["points"] for s in final["scores"])
        total_max = sum(c.max_points for c in request.criteria)
        llm_fraction = total / max(total_max, 1)
        similarity = tf_cosine(request.answer_text, request.exemplar_answer)
        agreement = 1.0 - min(1.0, abs(llm_fraction - similarity))

        confidence = round(0.7 * consistency + 0.3 * agreement, 2)
        raw = json.dumps({"grader": self.model_name, "prompt_version": PROMPT_VERSION,
                          "samples": samples, "consistency": consistency, "agreement": agreement})
        return final["scores"], confidence, final["feedback_text"], raw


def get_grader():
    if os.environ.get("ANTHROPIC_API_KEY"):
        return ClaudeGrader()
    return HeuristicGrader()
