using System.Text.Json;
using GradeLens.Api.Data;
using GradeLens.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GradeLens.Api.Services;

/// <summary>
/// Orchestrates a single submission through grading: state transitions,
/// confidence-based routing, and audit logging.
/// </summary>
public class GradingPipeline(GradeLensDbContext db, IGradingService grader, IConfiguration config)
{
    private double ConfidenceThreshold => config.GetValue("Grading:ConfidenceThreshold", 0.75);

    public async Task<Submission> ProcessAsync(Guid submissionId, CancellationToken ct = default)
    {
        var submission = await db.Submissions.FirstAsync(s => s.Id == submissionId, ct);
        var rubric = await db.Rubrics
            .Include(r => r.Criteria)
            .FirstAsync(r => r.AssignmentId == submission.AssignmentId, ct);

        Transition(submission, GradingStatus.Grading, "system", null);
        await db.SaveChangesAsync(ct);

        try
        {
            var result = await grader.GradeAsync(submission, rubric, ct);

            submission.Grade = new Grade
            {
                SubmissionId = submission.Id,
                Confidence = result.Confidence,
                FeedbackText = result.FeedbackText,
                CriterionScores = result.Scores.Select(s => new CriterionScore
                {
                    CriterionId = s.CriterionId,
                    Points = s.Points,
                    Justification = s.Justification
                }).ToList()
            };

            var target = result.Confidence >= ConfidenceThreshold
                ? GradingStatus.Published
                : GradingStatus.NeedsReview;

            Transition(submission, target, "system",
                JsonSerializer.Serialize(new { result.Confidence, threshold = ConfidenceThreshold, raw = result.RawModelResponse }));
        }
        catch (Exception ex)
        {
            Transition(submission, GradingStatus.Failed, "system",
                JsonSerializer.Serialize(new { error = ex.Message }));
        }

        await db.SaveChangesAsync(ct);
        return submission;
    }

    public async Task<Submission> OverrideAsync(Guid submissionId, string reviewer, string reason,
        IReadOnlyDictionary<Guid, int> revisedScores, CancellationToken ct = default)
    {
        var submission = await db.Submissions
            .Include(s => s.Grade!).ThenInclude(g => g.CriterionScores)
            .FirstAsync(s => s.Id == submissionId, ct);

        if (submission.Grade is null)
            throw new InvalidOperationException("Cannot override a submission that has no grade.");

        foreach (var score in submission.Grade.CriterionScores)
            if (revisedScores.TryGetValue(score.CriterionId, out var points))
                score.Points = points;

        submission.Grade.GradedBy = reviewer;
        Transition(submission, GradingStatus.Published, reviewer,
            JsonSerializer.Serialize(new { reason, revisedScores }));

        await db.SaveChangesAsync(ct);
        return submission;
    }

    private void Transition(Submission submission, GradingStatus to, string actor, string? detailsJson)
    {
        GradingStateMachine.EnsureTransition(submission.Status, to);
        db.AuditEntries.Add(new AuditEntry
        {
            SubmissionId = submission.Id,
            Action = $"{submission.Status} -> {to}",
            Actor = actor,
            DetailsJson = detailsJson
        });
        submission.Status = to;
    }
}
