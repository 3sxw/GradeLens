"""Measure grader agreement with human scores on the gold dataset.

Usage (from ai-service/):  python eval/run_eval.py

Uses whichever grader is active (Claude if ANTHROPIC_API_KEY is set, otherwise
the offline heuristic). Reports per-answer totals, mean absolute error, and the
share of answers graded within 2 points of the human total (out of 20).
Record results per prompt/grader version in docs/evals.md.
"""

import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from graders import get_grader
from main import CriterionIn, GradeRequest

WITHIN = 2


def main() -> None:
    data = json.loads((pathlib.Path(__file__).parent / "gold_dataset.json").read_text())
    criteria = [CriterionIn(**c) for c in data["criteria"]]
    grader = get_grader()
    total_max = sum(c.max_points for c in criteria)

    print(f"Grader: {grader.model_name} | {len(data['answers'])} gold answers | total_max={total_max}\n")
    print(f"{'id':<10}{'human':>7}{'ai':>6}{'abs_err':>9}")

    errors = []
    for answer in data["answers"]:
        request = GradeRequest(
            submission_id=answer["id"],
            question_text=data["question_text"],
            answer_text=answer["answer_text"],
            exemplar_answer=data["exemplar_answer"],
            criteria=criteria,
        )
        scores, confidence, _, _ = grader.grade(request)
        ai_total = sum(s["points"] for s in scores)
        human_total = sum(answer["human_scores"].values())
        err = abs(ai_total - human_total)
        errors.append(err)
        print(f"{answer['id']:<10}{human_total:>7}{ai_total:>6}{err:>9}")

    mae = sum(errors) / len(errors)
    within = sum(e <= WITHIN for e in errors) / len(errors)
    print(f"\nMAE (total /{total_max}): {mae:.2f}")
    print(f"Within ±{WITHIN} pts: {within:.0%}")


if __name__ == "__main__":
    main()
