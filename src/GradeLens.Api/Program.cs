using System.Text.Json.Serialization;
using GradeLens.Api.Data;
using GradeLens.Api.Domain;
using GradeLens.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GradeLensDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("GradeLens")));
builder.Services.AddHttpClient(); // IHttpClientFactory for the /system/grader probe
if (builder.Configuration.GetValue("Grading:Provider", "Ai") == "Stub")
{
    builder.Services.AddScoped<IGradingService, StubGradingService>();
}
else
{
    builder.Services.AddHttpClient<IGradingService, AiGradingService>(c =>
    {
        c.BaseAddress = new Uri(builder.Configuration.GetValue("Grading:AiServiceUrl", "http://localhost:8000")!);
        c.Timeout = TimeSpan.FromSeconds(120); // Claude self-consistency = multiple model calls
    });
}
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
app.UseDefaultFiles();
app.UseStaticFiles();

// --- Courses & assignments ---
app.MapPost("/courses", async (GradeLensDbContext db, Course course) =>
{
    db.Courses.Add(course);
    await db.SaveChangesAsync();
    return Results.Created($"/courses/{course.Id}", course);
});

app.MapGet("/courses", (GradeLensDbContext db) =>
    db.Courses
        .Include(c => c.Assignments)
        .ThenInclude(a => a.Rubric!)
        .ThenInclude(r => r.Criteria.OrderBy(cr => cr.SortOrder))
        .ToListAsync());

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

// --- System ---
// Proxies the AI service health check so the dashboard can show which grading
// engine is active (the browser cannot reach the AI service cross-origin).
app.MapGet("/system/grader", async (IConfiguration config, IHttpClientFactory httpFactory) =>
{
    var baseUrl = config.GetValue("Grading:AiServiceUrl", "http://localhost:8000")!;
    try
    {
        using var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);
        var health = await client.GetFromJsonAsync<Dictionary<string, string>>($"{baseUrl}/health");
        return Results.Ok(new { grader = health?["grader"] ?? "unknown" });
    }
    catch
    {
        return Results.Ok(new { grader = "offline" });
    }
});

// --- Audit ---
app.MapGet("/submissions/{id:guid}/audit", (GradeLensDbContext db, Guid id) =>
    db.AuditEntries.Where(a => a.SubmissionId == id).OrderBy(a => a.Timestamp).ToListAsync());

app.Run();

public record OverrideRequest(string Reviewer, string Reason, Dictionary<Guid, int> RevisedScores);
