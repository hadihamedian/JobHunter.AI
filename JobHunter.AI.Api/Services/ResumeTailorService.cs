using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.AI.Api.Models;

namespace JobHunter.AI.Api.Services;

public class ResumeTailorService(
    HttpClient httpClient,
    IConfiguration configuration,
    ResumeRepository resumeRepository)
{
    private const int MaxAttempts = 2; // اولیه + یک retry

    public async Task<TailorResult?> TailorAsync(TailorRequest request)
    {
        var resume = await resumeRepository.GetByIdAsync(request.ResumeId);
        if (resume is null) return null;

        var originalSignature = ResumeStructureValidator.Extract(resume.Content);
        var originalHeaderLines = ExtractHeaderLines(resume.Content);

        string? violationNotes = null;
        TailorResult? lastResult = null;
        var wasRetried = false;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = BuildPrompt(resume.Content, request, violationNotes);
            var result = await CallOllamaAsync(prompt);
            if (result is null) break;

            var lockedContent = ReplaceHeaderLines(result.TailoredContent, originalHeaderLines);
            result = result with { TailoredContent = lockedContent };
            lastResult = result;

            var tailoredSignature = ResumeStructureValidator.Extract(lockedContent);
            var violations = ResumeStructureValidator.Compare(originalSignature, tailoredSignature);
            violations.AddRange(ResumeStructureValidator.DetectDuplicateHeadings(lockedContent));

            if (violations.Count == 0) break;
            if (attempt == MaxAttempts) break;

            wasRetried = true;
            violationNotes = BuildViolationNotes(violations);
        }

        stopwatch.Stop();
        if (lastResult is null) return null;

        return lastResult with
        {
            WasRetried = wasRetried,
            DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1)
        };
    }

    private string BuildPrompt(string originalContent, TailorRequest request, string? violationNotes)
    {
        var styleInstruction = request.Style == "aggressive"
            ? "Aggressively rewrite sections to maximize keyword density. Add all relevant missing keywords naturally. Reorder skills by relevance to the job."
            : "Make conservative changes only. Keep the candidate's original voice and style. Only add clearly missing keywords and make minimal rewrites.";

        var retryBlock = violationNotes is null
            ? ""
            : $@"
 
IMPORTANT — YOUR PREVIOUS ATTEMPT DROPPED CONTENT. Fix these specific issues and do not repeat this mistake:
{violationNotes}
Every bullet, sub-role, and tech-stack item present in the Original Resume below MUST appear in your output,
even if you also add new keywords. Do not shorten lists to save space. Do NOT duplicate any section
heading (e.g. never output ""## Summary"" followed immediately by the word ""Summary"" again).";

        return $@"You are an expert ATS resume optimizer and professional resume formatter.
 
Style instruction: {styleInstruction}
{retryBlock}
 
Original Resume (Markdown):
{originalContent}
 
Job Description:
{request.JobDescription}
 
Task:
1. Rewrite the resume to better match the job description.
2. Keep the exact same section order and structure as the original — do not invent new sections or drop existing ones.
3. Add relevant keywords naturally, based only on skills/experience the candidate actually has.
4. Make achievements more specific and measurable where possible.
5. Keep it truthful — never invent employers, titles, dates, or skills not present in the original.
6. Do NOT drop any bullet, nested sub-role, or tech-stack item from the original — you may rewrite the
   wording, but the count of bullets per job and tech items per project must stay the same or increase.
 
OUTPUT FORMAT — the resume MUST follow this EXACT template and Markdown pattern:
 
# {{Full Name}}
**{{Title line}}**
{{Location}} | {{Email}} | Portfolio | GitHub | LinkedIn
 
## Summary
A single paragraph (3-5 sentences), no bullets. Write the heading ""## Summary"" exactly ONCE — never
repeat the word ""Summary"" as its own line before the paragraph.
 
## Work Experience
 
### {{Job Title}} at {{Company}} – {{Location}} (Remote)
*{{Start}} – {{End}}*
- Bullet
- Bullet
    - {{Sub-role Title}} ({{Start}}–{{End}}): one-line description — keep nested exactly like this,
      indented with 4 spaces before the dash, never promote to a top-level ### entry, never flatten
      into a plain top-level bullet with no indentation.
 
## Skills
**{{Category Name}}:** item1, item2, item3, item4
 
## Selected Projects
 
### {{Project Name}} — {{Tagline}} (Repository)
One paragraph.
`Tech1` · `Tech2` · `Tech3` · `Tech4`
 
## Education
**{{Institution}}**
{{Degree}}
 
STRICT RULES:
- Do NOT use tables, images, emoji, or raw HTML tags.
- Do NOT add horizontal rules (---) between sections.
- Do NOT repeat any section heading text as a plain line right after the heading itself.
- Skills lines must stay in '**Category:** item, item, item' form.
- Project tech-stack lines must stay in the '`Tech` · `Tech`' form, with the SAME NUMBER of items as the original.
 
Return ONLY this JSON object, no explanation, no markdown code fences around the JSON itself:
{{{{
   ""tailoredContent"": ""<the complete rewritten resume as a single Markdown string, using \\n for line breaks>"",
   ""keywordsAdded"": [""keyword1"", ""keyword2""],
   ""sectionsRewritten"": [""Summary — aligned to role"", ""Experience — added metrics""],
   ""estimatedAtsImprovement"": <number 1-30>
}}}}
 
Important: tailoredContent must be the COMPLETE resume in Markdown format, not plain text, and must follow the template above exactly.";
    }

    private static string BuildViolationNotes(List<string> violations) =>
        string.Join("\n", violations.Select(v => $"- {v}"));

    private static string ExtractHeaderLines(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Take(3));
    }

    private static string ReplaceHeaderLines(string tailoredMarkdown, string originalHeaderLines)
    {
        var lines = tailoredMarkdown.Replace("\r\n", "\n").Split('\n').ToList();
        var originalLines = originalHeaderLines.Split('\n');

        for (var i = 0; i < originalLines.Length && i < lines.Count; i++)
            lines[i] = originalLines[i];

        return string.Join("\n", lines);
    }

    private async Task<TailorResult?> CallOllamaAsync(string prompt)
    {
        var ollamaUrl = configuration["Ollama:Url"] ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "llama3.1:8b";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0.3 }
        };

        try
        {
            var response = await httpClient.PostAsJsonAsync(ollamaUrl, payload);
            response.EnsureSuccessStatusCode();

            var ollamaResp = await response.Content.ReadFromJsonAsync<OllamaResponse>();
            if (string.IsNullOrWhiteSpace(ollamaResp?.Response)) return null;

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<RawTailorResult>(ollamaResp.Response, jsonOptions);
            if (raw is null) return null;

            return new TailorResult(
                raw.TailoredContent,
                raw.KeywordsAdded,
                raw.SectionsRewritten,
                raw.EstimatedAtsImprovement
            );
        }
        catch
        {
            return null;
        }
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }

    private class RawTailorResult
    {
        public string TailoredContent { get; set; } = string.Empty;
        public List<string> KeywordsAdded { get; set; } = new();
        public List<string> SectionsRewritten { get; set; } = new();
        public int EstimatedAtsImprovement { get; set; }
    }
}