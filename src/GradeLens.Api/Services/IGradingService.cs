using GradeLens.Api.Domain;

namespace GradeLens.Api.Services;

public record GradingResult(
    IReadOnlyList<(Guid CriterionId, int Points, string Justification)> Scores,
    double Confidence,
    string FeedbackText,
    string RawModelResponse);

public interface IGradingService
{
    Task<GradingResult> GradeAsync(Submission submission, Rubric rubric, string questionText, CancellationToken ct = default);
}
