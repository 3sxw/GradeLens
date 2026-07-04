from graders import HeuristicGrader, tf_cosine, tokenize
from main import CriterionIn, GradeRequest


def make_request(answer: str) -> GradeRequest:
    return GradeRequest(
        submission_id="s1",
        question_text="Explain database normalization and the first three normal forms.",
        answer_text=answer,
        exemplar_answer=(
            "Normalization reduces redundancy and prevents update anomalies. "
            "1NF requires atomic values, 2NF removes partial dependencies on a "
            "composite key, 3NF removes transitive dependencies."
        ),
        criteria=[
            CriterionIn(id="c1", description="Explains purpose of normalization redundancy anomalies", max_points=4),
            CriterionIn(id="c2", description="Defines 1NF 2NF 3NF normal forms correctly", max_points=6),
        ],
    )


def test_tokenize_drops_stopwords():
    assert "the" not in tokenize("the purpose of the schema")
    assert "schema" in tokenize("the purpose of the schema")


def test_cosine_bounds():
    assert tf_cosine("abc def", "abc def") > 0.99
    assert tf_cosine("abc", "xyz") == 0.0
    assert tf_cosine("", "abc") == 0.0


def test_strong_answer_outscores_weak_answer():
    grader = HeuristicGrader()
    strong, _, _, _ = grader.grade(make_request(
        "Normalization reduces redundancy and prevents update anomalies. "
        "1NF needs atomic values, 2NF removes partial dependencies, 3NF removes transitive dependencies."
    ))
    weak, _, _, _ = grader.grade(make_request("Databases are useful for storing information quickly."))
    assert sum(s["points"] for s in strong) > sum(s["points"] for s in weak)


def test_scores_within_bounds_and_confidence_valid():
    grader = HeuristicGrader()
    request = make_request("Normalization reduces redundancy. 1NF atomic values.")
    scores, confidence, feedback, raw = grader.grade(request)
    limits = {c.id: c.max_points for c in request.criteria}
    for s in scores:
        assert 0 <= s["points"] <= limits[s["criterion_id"]]
    assert 0.0 <= confidence <= 1.0
    assert feedback


def test_deterministic():
    grader = HeuristicGrader()
    request = make_request("Normalization reduces redundancy and anomalies.")
    assert grader.grade(request) == grader.grade(request)
