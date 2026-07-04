using System.Text.Json;
using GradeLens.Api.Domain;

namespace GradeLens.Api.Services;

/// <summary>
/// Calls the Python AI grading service. The service uses Claude when an
/// ANTHROPIC_API_KEY is configured on it, otherwise an offline heuristic —
/// this side is agnostic and just validates the contract.
/// </summary>
public class AiGradingService(HttpClient http) : IGradingService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<GradingResult> GradeAsync(Submission submission, Rubric rubric, string questionText, CancellationToken ct = default)
    {
        var request = new
        {
            SubmissionId = submission.Id.ToString(),
            QuestionText = questionText,
            AnswerText = submission.AnswerText,
            ExemplarAnswer = rubric.ExemplarAnswer,
            Criteria = rubric.Criteria
                .OrderBy(c => c.SortOrder)
                .Select(c => new { Id = c.Id.ToString(), c.Description, MaxPoints = c.MaxPoints })
        };

        var response = await http.PostAsJsonAsync("/grade", request, Json, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GradeResponseDto>(Json, ct)
                   ?? throw new InvalidOperationException("AI service returned an empty body.");

        var maxByCriterion = rubric.Criteria.ToDictionary(c => c.Id, c => c.MaxPoints);
        var scores = body.Scores.Select(s =>
        {
            var criterionId = Guid.Parse(s.CriterionId);
            if (!maxByCriterion.TryGetValue(criterionId, out var max))
                throw new InvalidOperationException($"AI service scored unknown criterion {criterionId}.");
            if (s.Points < 0 || s.Points > max)
                throw new InvalidOperationException($"AI service points {s.Points} out of range for criterion {criterionId}.");
            return (criterionId, s.Points, s.Justification);
        }).ToList();

        if (scores.Select(s => s.criterionId).ToHashSet().Count != rubric.Criteria.Count)
            throw new InvalidOperationException("AI service did not score every criterion exactly once.");

        return new GradingResult(scores, body.Confidence, body.FeedbackText, body.RawModelResponse);
    }

    private record GradeResponseDto(
        List<ScoreDto> Scores, double Confidence, string FeedbackText, string Grader, string RawModelResponse);

    private record ScoreDto(string CriterionId, int Points, string Justification);
}
