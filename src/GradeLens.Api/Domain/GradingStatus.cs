namespace GradeLens.Api.Domain;

public enum GradingStatus
{
    Pending,
    Grading,
    NeedsReview,
    Published,
    Failed
}

/// <summary>
/// Central authority on legal submission state transitions. Grades must never
/// silently jump states (e.g. Pending -> Published) — every path is explicit
/// so the audit trail always matches reality.
/// </summary>
public static class GradingStateMachine
{
    private static readonly Dictionary<GradingStatus, GradingStatus[]> Allowed = new()
    {
        [GradingStatus.Pending] = [GradingStatus.Grading],
        [GradingStatus.Grading] = [GradingStatus.NeedsReview, GradingStatus.Published, GradingStatus.Failed],
        [GradingStatus.NeedsReview] = [GradingStatus.Published, GradingStatus.Grading],
        [GradingStatus.Failed] = [GradingStatus.Grading],
        [GradingStatus.Published] = []
    };

    public static bool CanTransition(GradingStatus from, GradingStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static void EnsureTransition(GradingStatus from, GradingStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"Illegal grading transition: {from} -> {to}");
    }
}
