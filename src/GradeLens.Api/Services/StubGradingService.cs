using GradeLens.Api.Domain;

namespace GradeLens.Api.Services;

/// <summary>
/// Week-1 placeholder so the full pipeline (enqueue -> grade -> route -> publish/review)
/// can be built and tested end-to-end before the AI service exists. Replaced in Week 2
/// by AiGradingService, which calls the Python FastAPI service.
/// </summary>
public class StubGradingService : IGradingService
{
    public Task<GradingResult> GradeAsync(Submission submission, Rubric rubric, string questionText, CancellationToken ct = default)
    {
        var rng = new Random(submission.Id.GetHashCode());
        var scores = rubric.Criteria
            .Select(c => (c.Id, rng.Next(0, c.MaxPoints + 1), "Stub justification — replace with AI output."))
            .ToList();

        // Random confidence exercises both routing paths (auto-publish vs review queue).
        var confidence = Math.Round(rng.NextDouble(), 2);

        return Task.FromResult(new GradingResult(
            scores,
            confidence,
            "Stub feedback: this grade was produced by the placeholder grader.",
            RawModelResponse: "{}"));
    }
}
