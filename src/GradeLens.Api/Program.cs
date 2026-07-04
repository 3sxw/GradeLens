using System.Text.Json.Serialization;
using GradeLens.Api.Data;
using GradeLens.Api.Domain;
using GradeLens.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GradeLensDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("GradeLens")));
builder.Services.AddScoped<IGradingService, StubGradingService>();
builder.Services.AddScoped<GradingPipeline>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GradeLensDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.UseSwagger();
app.UseSwaggerUI();

// --- Courses & assignments ---
app.MapPost("/courses", async (GradeLensDbContext db, Course course) =>
{
    db.Courses.Add(course);
    await db.SaveChangesAsync();
    return Results.Created($"/courses/{course.Id}", course);
});

app.MapGet("/courses", (GradeLensDbContext db) =>
    db.Courses.Include(c => c.Assignments).ToListAsync());

// --- Rubrics ---
app.MapPost("/assignments/{assignmentId:guid}/rubric", async (GradeLensDbContext db, Guid assignmentId, Rubric rubric) =>
{
    rubric.AssignmentId = assignmentId;
    db.Rubrics.Add(rubric);
    await db.SaveChangesAsync();
    return Results.Created($"/assignments/{assignmentId}/rubric", rubric);
});

// --- Submissions ---
app.MapPost("/assignments/{assignmentId:guid}/submissions", async (GradeLensDbContext db, Guid assignmentId, Submission submission) =>
{
    submission.AssignmentId = assignmentId;
    db.Submissions.Add(submission);
    await db.SaveChangesAsync();
    return Results.Created($"/submissions/{submission.Id}", submission);
});

app.MapGet("/assignments/{assignmentId:guid}/submissions", (GradeLensDbContext db, Guid assignmentId) =>
    db.Submissions
        .Where(s => s.AssignmentId == assignmentId)
        .Include(s => s.Grade!).ThenInclude(g => g.CriterionScores)
        .ToListAsync());

app.MapGet("/submissions/{id:guid}", async (GradeLensDbContext db, Guid id) =>
    await db.Submissions
        .Include(s => s.Grade!).ThenInclude(g => g.CriterionScores)
        .FirstOrDefaultAsync(s => s.Id == id)
        is { } s ? Results.Ok(s) : Results.NotFound());

// --- Grading ---
app.MapPost("/submissions/{id:guid}/grade", (GradingPipeline pipeline, Guid id) =>
    pipeline.ProcessAsync(id));

app.MapGet("/review-queue", (GradeLensDbContext db) =>
    db.Submissions
        .Where(s => s.Status == GradingStatus.NeedsReview)
        .Include(s => s.Grade!).ThenInclude(g => g.CriterionScores)
        .ToListAsync());

app.MapPost("/submissions/{id:guid}/override", (GradingPipeline pipeline, Guid id, OverrideRequest request) =>
    pipeline.OverrideAsync(id, request.Reviewer, request.Reason, request.RevisedScores));

// --- Audit ---
app.MapGet("/submissions/{id:guid}/audit", (GradeLensDbContext db, Guid id) =>
    db.AuditEntries.Where(a => a.SubmissionId == id).OrderBy(a => a.Timestamp).ToListAsync());

app.Run();

public record OverrideRequest(string Reviewer, string Reason, Dictionary<Guid, int> RevisedScores);
