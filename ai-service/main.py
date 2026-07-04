"""GradeLens AI grading service (Week 2 skeleton).

Receives a submission + rubric from the .NET API, calls Claude with a
rubric-constrained structured-output prompt, validates the response, and
returns per-criterion scores with a confidence estimate.
"""

from fastapi import FastAPI
from pydantic import BaseModel, Field

app = FastAPI(title="GradeLens AI Service")


class CriterionIn(BaseModel):
    id: str
    description: str
    max_points: int


class GradeRequest(BaseModel):
    submission_id: str
    question_text: str
    answer_text: str
    exemplar_answer: str
    criteria: list[CriterionIn]


class CriterionScore(BaseModel):
    criterion_id: str
    points: int
    justification: str


class GradeResponse(BaseModel):
    scores: list[CriterionScore]
    confidence: float = Field(ge=0.0, le=1.0)
    feedback_text: str
    raw_model_response: str


@app.get("/health")
def health() -> dict:
    return {"status": "ok"}


@app.post("/grade", response_model=GradeResponse)
def grade(request: GradeRequest) -> GradeResponse:
    # TODO Week 2:
    #   1. Assemble rubric-constrained prompt (see prompts/grading_v1.md).
    #   2. Call Claude (claude-sonnet-5) with tool-use to force structured JSON.
    #   3. Validate scores against criterion max_points; retry on violations.
    #   4. Self-consistency: sample N=3, confidence = 1 - normalized score spread.
    #   5. Embedding similarity vs exemplar_answer as an independent signal.
    return GradeResponse(
        scores=[
            CriterionScore(
                criterion_id=c.id,
                points=0,
                justification="AI grading not yet implemented.",
            )
            for c in request.criteria
        ],
        confidence=0.0,
        feedback_text="AI grading not yet implemented.",
        raw_model_response="{}",
    )
