using JobAssistant.Api.Models;
using JobAssistant.Api.Services;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddScoped<AiAnalyzerService>();
builder.Services.AddScoped<CareerChatService>(); // <-- اضافه شد
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5150")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Host=localhost;Database=jobassistant;Username=postgres;Password=postgres";
var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddScoped<ApplicationRepository>();
builder.Services.AddScoped<DataChatService>();
builder.Services.AddScoped<ResumeRepository>();
builder.Services.AddScoped<ResumeRecommendationService>();

var app = builder.Build();
app.UseCors();

app.MapPost("/api/analyze", async (AnalyzeRequest request, AiAnalyzerService analyzer) =>
{
    if (string.IsNullOrWhiteSpace(request.ResumeContent) || 
        string.IsNullOrWhiteSpace(request.JobDescription))
        return Results.BadRequest("Resume and job description are required.");

    var result = await analyzer.AnalyzeAsync(request.ResumeContent, request.JobDescription);
    return result is null ? Results.Problem("Failed to analyze") : Results.Ok(result);
});

app.MapPost("/api/analyze-by-id", async (
    AnalyzeByIdRequest req,
    ResumeRepository resumeRepo,
    AiAnalyzerService analyzer) =>
{
    if (req.ResumeId == Guid.Empty || string.IsNullOrWhiteSpace(req.JobDescription))
        return Results.BadRequest("ResumeId and JobDescription are required.");

    var resume = await resumeRepo.GetByIdAsync(req.ResumeId);
    if (resume is null) return Results.NotFound("Resume not found.");

    var result = await analyzer.AnalyzeAsync(resume.Content, req.JobDescription);
    return result is null ? Results.Problem("Failed to analyze") : Results.Ok(result);
});

// <-- Endpoint جدید اضافه شد
app.MapPost("/api/chat", async (ChatRequest request, CareerChatService chatService) =>
{
    if (string.IsNullOrWhiteSpace(request.UserMessage))
        return Results.BadRequest("Message is required.");

    var response = await chatService.ChatAsync(request);
    return Results.Ok(response);
});

var apps = app.MapGroup("/api/applications");

apps.MapGet("/", async (ApplicationRepository repo, string? status = null) =>
    Results.Ok(await repo.GetAllAsync(status)));

apps.MapGet("/stats", async (ApplicationRepository repo) =>
    Results.Ok(await repo.GetStatsAsync()));

apps.MapGet("/{id:guid}", async (Guid id, ApplicationRepository repo) =>
{
    var appData = await repo.GetByIdAsync(id);
    return appData is null ? Results.NotFound() : Results.Ok(appData);
});

apps.MapPost("/", async (CreateApplicationRequest req, ApplicationRepository repo) =>
{
    var id = await repo.CreateAsync(req);
    return Results.Created($"/api/applications/{id}", new { id });
});

apps.MapPatch("/{id:guid}", async (Guid id, UpdateApplicationRequest req, ApplicationRepository repo) =>
{
    var ok = await repo.UpdateAsync(id, req);
    return ok ? Results.NoContent() : Results.NotFound();
});

apps.MapDelete("/{id:guid}", async (Guid id, ApplicationRepository repo) =>
{
    var ok = await repo.DeleteAsync(id);
    return ok ? Results.NoContent() : Results.NotFound();
});

apps.MapPost("/chat", async (DataChatRequest req, DataChatService chatSvc) =>
    Results.Ok(await chatSvc.ChatAsync(req)));

apps.MapPost("/{id:guid}/move-up", async (Guid id, ApplicationRepository repo) =>
{
    var ok = await repo.MoveUpAsync(id);
    return ok ? Results.NoContent() : Results.BadRequest();
});

apps.MapPost("/{id:guid}/move-down", async (Guid id, ApplicationRepository repo) =>
{
    var ok = await repo.MoveDownAsync(id);
    return ok ? Results.NoContent() : Results.BadRequest();
});

var resumes = app.MapGroup("/api/resumes");

// ✅ route‌های خاص (literal) اول
resumes.MapGet("/", async (ResumeRepository repo) =>
    Results.Ok(await repo.GetAllAsync()));

resumes.MapPost("/", async (CreateResumeRequest req, ResumeRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Content))
        return Results.BadRequest("Name and Content are required.");
    var id = await repo.CreateAsync(req);
    return Results.Created($"/api/resumes/{id}", new { id });
});

resumes.MapPost("/recommend", async (
    RecommendResumeRequest req,
    ResumeRecommendationService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.JobDescription))
        return Results.BadRequest("Job description is required.");
    var result = await svc.RecommendAsync(req.JobDescription);
    return result is null
        ? Results.NotFound("No resumes found.")
        : Results.Ok(result);
});

// ✅ route‌های عمومی (parameter) آخر
resumes.MapGet("/{id:guid}", async (Guid id, ResumeRepository repo) =>
{
    var resume = await repo.GetByIdAsync(id);
    return resume is null ? Results.NotFound() : Results.Ok(resume);
});

resumes.MapPatch("/{id:guid}", async (Guid id, UpdateResumeRequest req, ResumeRepository repo) =>
{
    var ok = await repo.UpdateAsync(id, req);
    return ok ? Results.NoContent() : Results.NotFound();
});

resumes.MapDelete("/{id:guid}", async (Guid id, ResumeRepository repo) =>
{
    var ok = await repo.DeleteAsync(id);
    return ok ? Results.NoContent() : Results.NotFound();
});

await ResumeRepository.InitDbAsync(dataSource);
await ApplicationRepository.InitDbAsync(dataSource);

app.Run();