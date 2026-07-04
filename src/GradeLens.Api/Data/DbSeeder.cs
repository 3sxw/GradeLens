using GradeLens.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GradeLens.Api.Data;

/// <summary>
/// Seeds a demo course with a rubric and a few submissions so every endpoint
/// can be exercised immediately after `docker compose up`. Idempotent: skips
/// seeding if any course exists.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(GradeLensDbContext db)
    {
        if (await db.Courses.AnyAsync())
            return;

        var assignment = new Assignment
        {
            Id = Guid.NewGuid(),
            Title = "Normalization essay",
            QuestionText = "Explain the purpose of database normalization and describe the first three normal forms with an example of a violation of each."
        };

        var course = new Course
        {
            Id = Guid.NewGuid(),
            Name = "CS201 - Database Systems",
            Assignments = [assignment]
        };

        var rubric = new Rubric
        {
            Id = Guid.NewGuid(),
            AssignmentId = assignment.Id,
            ExemplarAnswer =
                "Normalization organizes relational data to reduce redundancy and prevent update, insert, and delete anomalies. " +
                "1NF requires atomic column values and no repeating groups — storing a comma-separated list of phone numbers in one column violates it. " +
                "2NF requires 1NF plus no partial dependency of non-key attributes on part of a composite key — in an OrderItems(OrderId, ProductId, ProductName) table, ProductName depends only on ProductId, violating 2NF. " +
                "3NF requires 2NF plus no transitive dependencies — an Employees table storing DepartmentId and DepartmentName violates 3NF because DepartmentName depends on DepartmentId, not the employee key.",
            Criteria =
            [
                new Criterion { Id = Guid.NewGuid(), Description = "Explains the purpose of normalization (redundancy, anomalies)", MaxPoints = 4, SortOrder = 1 },
                new Criterion { Id = Guid.NewGuid(), Description = "Correctly defines 1NF, 2NF, and 3NF", MaxPoints = 6, SortOrder = 2 },
                new Criterion { Id = Guid.NewGuid(), Description = "Gives a valid violation example for each normal form", MaxPoints = 6, SortOrder = 3 },
                new Criterion { Id = Guid.NewGuid(), Description = "Clarity and technical accuracy of writing", MaxPoints = 4, SortOrder = 4 }
            ]
        };

        List<Submission> submissions =
        [
            new()
            {
                Id = Guid.NewGuid(),
                AssignmentId = assignment.Id,
                StudentIdentifier = "S1001",
                AnswerText =
                    "Normalization is about structuring tables so the same fact is stored only once, which prevents update and delete anomalies. " +
                    "1NF means every column holds a single atomic value; putting several phone numbers in one cell breaks it. " +
                    "2NF says every non-key column must depend on the whole composite key; a ProductName column in an order-lines table keyed by (OrderId, ProductId) breaks it. " +
                    "3NF forbids transitive dependencies; storing a department's name next to its id in the employee table breaks it."
            },
            new()
            {
                Id = Guid.NewGuid(),
                AssignmentId = assignment.Id,
                StudentIdentifier = "S1002",
                AnswerText =
                    "Normalization makes the database faster by splitting tables. 1NF is when the table has a primary key. " +
                    "2NF is when it has foreign keys. 3NF is when all the tables are joined. For example a students table with a key is in 1NF."
            },
            new()
            {
                Id = Guid.NewGuid(),
                AssignmentId = assignment.Id,
                StudentIdentifier = "S1003",
                AnswerText =
                    "The goal of normalization is reducing duplicated data so updates only happen in one place. " +
                    "First normal form requires atomic values. Second normal form removes partial dependencies on a composite key. " +
                    "Third normal form removes transitive dependencies, like storing a city's country in a customers table."
            }
        ];

        // Seed one submission already graded and routed to NeedsReview so the
        // review queue (Approve/Override UI) is testable immediately after setup.
        var criteria = rubric.Criteria;
        var needsReview = new Submission
        {
            Id = Guid.NewGuid(),
            AssignmentId = assignment.Id,
            StudentIdentifier = "S1004",
            AnswerText =
                "Normalization means organizing tables to avoid repeating data. 1NF is about atomic values I believe. " +
                "2NF and 3NF both deal with dependencies between columns but I always mix up which is which. " +
                "An example violation is a table storing several phone numbers in one cell.",
            Status = GradingStatus.NeedsReview,
            Grade = new Grade
            {
                Confidence = 0.58,
                FeedbackText = "Partial understanding: 1NF and its example are correct, but 2NF/3NF are conflated. Review partial vs transitive dependencies.",
                GradedBy = "ai",
                CriterionScores =
                [
                    new CriterionScore { CriterionId = criteria[0].Id, Points = 2, Justification = "Mentions avoiding repeated data but not anomalies." },
                    new CriterionScore { CriterionId = criteria[1].Id, Points = 2, Justification = "1NF acceptable; 2NF and 3NF admitted to be confused." },
                    new CriterionScore { CriterionId = criteria[2].Id, Points = 2, Justification = "One valid example (1NF); none for 2NF/3NF." },
                    new CriterionScore { CriterionId = criteria[3].Id, Points = 2, Justification = "Readable but hedged and imprecise." }
                ]
            }
        };
        submissions.Add(needsReview);

        db.Courses.Add(course);
        db.Rubrics.Add(rubric);
        db.Submissions.AddRange(submissions);
        db.AuditEntries.AddRange(
            new AuditEntry { SubmissionId = needsReview.Id, Action = "Pending -> Grading", Actor = "system" },
            new AuditEntry { SubmissionId = needsReview.Id, Action = "Grading -> NeedsReview", Actor = "system",
                             DetailsJson = """{"Confidence":0.58,"threshold":0.75,"seeded":true}""" });
        await db.SaveChangesAsync();
    }
}
