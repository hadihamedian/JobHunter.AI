using JobAssistant.Api.Models;
using JobAssistant.Api.Services;

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

// <-- Endpoint جدید اضافه شد
app.MapPost("/api/chat", async (ChatRequest request, CareerChatService chatService) =>
{
    if (string.IsNullOrWhiteSpace(request.UserMessage))
        return Results.BadRequest("Message is required.");

    var response = await chatService.ChatAsync(request);
    return Results.Ok(response);
});

app.Run();