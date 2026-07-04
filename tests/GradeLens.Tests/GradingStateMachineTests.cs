using GradeLens.Api.Domain;

namespace GradeLens.Tests;

public class GradingStateMachineTests
{
    [Theory]
    [InlineData(GradingStatus.Pending, GradingStatus.Grading)]
    [InlineData(GradingStatus.Grading, GradingStatus.Published)]
    [InlineData(GradingStatus.Grading, GradingStatus.NeedsReview)]
    [InlineData(GradingStatus.Grading, GradingStatus.Failed)]
    [InlineData(GradingStatus.NeedsReview, GradingStatus.Published)]
    [InlineData(GradingStatus.NeedsReview, GradingStatus.Grading)]
    [InlineData(GradingStatus.Failed, GradingStatus.Grading)]
    public void AllowsLegalTransitions(GradingStatus from, GradingStatus to) =>
        Assert.True(GradingStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(GradingStatus.Pending, GradingStatus.Published)]
    [InlineData(GradingStatus.Pending, GradingStatus.NeedsReview)]
    [InlineData(GradingStatus.Published, GradingStatus.Grading)]
    [InlineData(GradingStatus.Published, GradingStatus.NeedsReview)]
    [InlineData(GradingStatus.Grading, GradingStatus.Pending)]
    public void RejectsIllegalTransitions(GradingStatus from, GradingStatus to)
    {
        Assert.False(GradingStateMachine.CanTransition(from, to));
        Assert.Throws<InvalidOperationException>(() => GradingStateMachine.EnsureTransition(from, to));
    }

    [Fact]
    public void PublishedIsTerminal()
    {
        foreach (var target in Enum.GetValues<GradingStatus>())
            Assert.False(GradingStateMachine.CanTransition(GradingStatus.Published, target));
    }
}
