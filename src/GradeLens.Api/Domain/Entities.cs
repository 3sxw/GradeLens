namespace GradeLens.Api.Domain;

public class Course
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public List<Assignment> Assignments { get; set; } = [];
}

public class Assignment
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public required string Title { get; set; }
    public required string QuestionText { get; set; }
    public Rubric? Rubric { get; set; }
    public List<Submission> Submissions { get; set; } = [];
}

public class Rubric
{
    public Guid Id { get; set; }
    public Guid AssignmentId { get; set; }
    /// <summary>Model answer used both for LLM context and embedding similarity.</summary>
    public required string ExemplarAnswer { get; set; }
    public List<Criterion> Criteria { get; set; } = [];
}

public class Criterion
{
    public Guid Id { get; set; }
    public Guid RubricId { get; set; }
    public required string Description { get; set; }
    public int MaxPoints { get; set; }
    public int SortOrder { get; set; }
}

public class Submission
{
    public Guid Id { get; set; }
    public Guid AssignmentId { get; set; }
    public required string StudentIdentifier { get; set; }
    public required string AnswerText { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public GradingStatus Status { get; set; } = GradingStatus.Pending;
    public Grade? Grade { get; set; }
}

public class Grade
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public List<CriterionScore> CriterionScores { get; set; } = [];
    /// <summary>0.0–1.0; below the routing threshold sends the submission to review.</summary>
    public double Confidence { get; set; }
    public required string FeedbackText { get; set; }
    public string GradedBy { get; set; } = "ai"; // "ai" or reviewer identifier
    public DateTimeOffset GradedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CriterionScore
{
    public Guid Id { get; set; }
    public Guid GradeId { get; set; }
    public Guid CriterionId { get; set; }
    public int Points { get; set; }
    public required string Justification { get; set; }
}

public class AuditEntry
{
    public long Id { get; set; }
    public Guid SubmissionId { get; set; }
    public required string Action { get; set; }
    public required string Actor { get; set; }
    /// <summary>Raw payload: prompt version, model id, raw AI response, override reason, etc.</summary>
    public string? DetailsJson { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
