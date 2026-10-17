using JobHunter.AI.Api.Agents;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;
using JobHunter.AI.Api.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Npgsql;
using OllamaSharp;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatResponse = Microsoft.Extensions.AI.ChatResponse;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                "http://localhost:5150",
                "http://localhost:5065",
                "http://localhost:80"
            )
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
var ollamaModel = builder.Configuration["Ollama:Model"] ?? "llama3.2:latest";
var numCtx = builder.Configuration.GetValue<int>("Ollama:NumCtx", 8192);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Host=localhost;Database=jobassistant;Username=postgres;Password=postgres";
var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);

// Repositories
builder.Services.AddScoped<ApplicationRepository>();
builder.Services.AddScoped<ResumeRepository>();
builder.Services.AddScoped<InterviewRepository>();

// Deterministic Tools
builder.Services.AddScoped<ApplicationDataTools>();
builder.Services.AddScoped<AtsEvaluationTools>();
builder.Services.AddScoped<InterviewBankTools>();
builder.Services.AddScoped<CareerAdvisorTools>();

// Single IChatClient backed by OllamaSharp with context window middleware
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var ollamaClient = new OllamaApiClient(new Uri(ollamaEndpoint), ollamaModel);

    return new ChatClientBuilder(ollamaClient)
        .Use(inner => new OllamaNumCtxChatClient(inner, numCtx))
        .Build();
});

// Deterministic AI & Pipeline Services
builder.Services.AddScoped<AiAnalyzerService>();
builder.Services.AddScoped<CareerChatService>(); 
builder.Services.AddScoped<InterviewGeneratorService>();
builder.Services.AddScoped<InterviewBankChatService>();
builder.Services.AddScoped<ResumeRecommendationService>();
builder.Services.AddScoped<ResumeTailorService>();
builder.Services.AddScoped<SemanticSearchService>();
builder.Services.AddScoped<CareerAdvisorAgent>();
builder.Services.AddScoped<DataChatAgent>();

var app = builder.Build();
app.UseRouting();
app.UseCors();

// === CONVERSATIONAL AGENT ENDPOINTS ===

app.MapPost("/api/career-chat", async (CareerChatRequest request, CareerAdvisorAgent agent) =>
{
    if (string.IsNullOrWhiteSpace(request.UserMessage))
        return Results.BadRequest("Message is required.");

    var response = await agent.RunAsync(request);
    return Results.Ok(response);
});

app.MapPost("/api/applications/chat", async (DataChatRequest req, DataChatAgent agent) =>
{
    var result = await agent.RunAsync(req);
    return Results.Ok(result);
});

// === DETERMINISTIC PIPELINE ENDPOINTS ===

app.MapPost("/api/analyze", async (AnalyzeRequest request, [FromServices] AiAnalyzerService analyzer) =>
{
    if (string.IsNullOrWhiteSpace(request.ResumeContent) || string.IsNullOrWhiteSpace(request.JobDescription))
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

app.MapPost("/api/tailor", async (TailorRequest request, [FromServices] ResumeTailorService tailorService) =>
{
    if (request.ResumeId == Guid.Empty || string.IsNullOrWhiteSpace(request.JobDescription))
        return Results.BadRequest("ResumeId and JobDescription are required.");

    var result = await tailorService.TailorAsync(request);
    return result is null ? Results.NotFound("Resume not found or AI failed.") : Results.Ok(result);
});

app.MapPost("/api/interviews/chat", async (InterviewBankChatRequest req, InterviewBankChatService bankChatService) =>
{
    var result = await bankChatService.ChatAsync(req);
    return Results.Ok(result);
});

app.MapPost("/api/chat", async (ChatRequest request, CareerChatService chatService) =>
{
    if (string.IsNullOrWhiteSpace(request.UserMessage))
        return Results.BadRequest("Message is required.");

    var response = await chatService.ChatAsync(request);
    return Results.Ok(response);
});

app.MapPost("/semantic-search", async (SemanticSearchRequest req, SemanticSearchService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Query))
        return Results.BadRequest("Query is required.");
    var result = await svc.SearchAsync(req.Query);
    return result is null
        ? Results.Problem("AI search failed. Make sure Ollama is running.", statusCode: 500)
        : Results.Ok(result);
});

// === APPLICATION MANAGEMENT ENDPOINTS ===

var apps = app.MapGroup("/api/applications");
apps.MapGet("/", async (ApplicationRepository repo, string? status = null, string? q = null) =>
    Results.Ok(await repo.GetAllAsync(status, q)));
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
resumes.MapGet("/", async (ResumeRepository repo) =>
    Results.Ok(await repo.GetAllAsync()));
resumes.MapPost("/", async (CreateResumeRequest req, ResumeRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Content))
        return Results.BadRequest("Name and Content are required.");
    var id = await repo.CreateAsync(req);
    return Results.Created($"/api/resumes/{id}", new { id });
});
resumes.MapPost("/recommend", async (RecommendResumeRequest req, ResumeRecommendationService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.JobDescription))
        return Results.BadRequest("Job description is required.");
    var result = await svc.RecommendAsync(req.JobDescription);
    return result is null ? Results.NotFound("No resumes found.") : Results.Ok(result);
});
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
resumes.MapGet("/{id:guid}/usage", async (Guid id, ApplicationRepository appRepo) =>
    Results.Ok(new ResumeUsage(await appRepo.GetByResumeIdAsync(id))));
resumes.MapDelete("/{id:guid}", async (Guid id, ResumeRepository repo, ApplicationRepository appRepo) =>
{
    var usage = await appRepo.GetByResumeIdAsync(id);
    if (usage.Count > 0)
        return Results.Conflict(new ResumeUsage(usage));

    try
    {
        var ok = await repo.DeleteAsync(id);
        return ok ? Results.NoContent() : Results.NotFound();
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
    {
        var freshUsage = await appRepo.GetByResumeIdAsync(id);
        return Results.Conflict(new ResumeUsage(freshUsage));
    }
});

var interviews = app.MapGroup("/api/interviews");
interviews.MapGet("/application/{applicationId:guid}", async (Guid applicationId, InterviewRepository repo) =>
    Results.Ok(await repo.GetByApplicationAsync(applicationId)));
interviews.MapGet("/", async (InterviewRepository repo) =>
    Results.Ok(await repo.GetAllAsync()));
interviews.MapPost("/generate", async (GenerateQuestionsRequest req, InterviewGeneratorService svc) =>
{
    if (req.ApplicationId == Guid.Empty)
        return Results.BadRequest("ApplicationId is required.");
    var questions = await svc.GenerateAsync(req);
    return Results.Ok(questions);
});
interviews.MapPatch("/{id:guid}", async (Guid id, UpdateQuestionRequest req, InterviewRepository repo) =>
{
    var ok = await repo.UpdateAsync(id, req);
    return ok ? Results.NoContent() : Results.NotFound();
});
interviews.MapDelete("/application/{applicationId:guid}", async (Guid applicationId, InterviewRepository repo) =>
{
    await repo.DeleteByApplicationAsync(applicationId);
    return Results.NoContent();
});

await ResumeRepository.InitDbAsync(dataSource);
await InterviewRepository.InitDbAsync(dataSource);
await ApplicationRepository.InitDbAsync(dataSource);

app.Run();

public class OllamaNumCtxChatClient(IChatClient innerClient, int numCtx) : DelegatingChatClient(innerClient)
{
    public override Task<AiChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();

        if (!options.AdditionalProperties.ContainsKey("num_ctx"))
        {
            options.AdditionalProperties["num_ctx"] = numCtx;
        }

        return base.GetResponseAsync(chatMessages, options, cancellationToken);
    }
}