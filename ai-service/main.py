"""GradeLens AI grading service.

Receives a submission + rubric from the .NET API and returns per-criterion
scores with a confidence estimate. Uses Claude when ANTHROPIC_API_KEY is set,
otherwise falls back to a deterministic offline heuristic grader — see
graders.py for both implementations.
"""

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from graders import get_grader

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
    grader: str
    raw_model_response: str


@app.get("/health")
def health() -> dict:
    return {"status": "ok", "grader": get_grader().model_name}


@app.post("/grade", response_model=GradeResponse)
def grade(request: GradeRequest) -> GradeResponse:
    if not request.criteria:
        raise HTTPException(status_code=422, detail="Rubric has no criteria.")

    grader = get_grader()
    try:
        scores, confidence, feedback, raw = grader.grade(request)
    except Exception as exc:  # surfaced to the .NET side, which marks the submission Failed
        raise HTTPException(status_code=502, detail=f"Grading failed: {exc}") from exc

    return GradeResponse(
        scores=[CriterionScore(**s) for s in scores],
        confidence=min(1.0, max(0.0, confidence)),
        feedback_text=feedback,
        grader=grader.model_name,
        raw_model_response=raw,
    )
