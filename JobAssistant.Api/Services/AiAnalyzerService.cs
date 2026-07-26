using System.Text.Json;
using System.Text.Json.Serialization;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class AiAnalyzerService(HttpClient httpClient, IConfiguration configuration)
{
    public async Task<AnalyzeResult?> AnalyzeAsync(string resume, string jobDesc)
    {
        // Stage 1: figure out what the job actually asks for — without the resume
        // anywhere near the prompt, so the model has nothing to blend it with.
        var requirements = await ExtractRequirementsAsync(jobDesc);
        if (requirements is null || requirements.Count == 0) return null;

        // Stage 2: "skill" requirements are checked with plain, deterministic text
        // matching in C# — not by asking a small local model, which tends to miss
        // obvious literal matches once the list/resume gets long. Only "experience"
        // requirements (which genuinely need date-range reasoning) go to the model.
        var matchingSkills = new List<string>();
        var missingSkills = new List<string>();
        var experienceRequirements = new List<RequirementItem>();

        foreach (var req in requirements)
        {
            if (req.Type == "experience")
            {
                experienceRequirements.Add(req);
                continue;
            }

            if (ResumeContainsSkill(resume, req.Text))
                matchingSkills.Add(req.Text);
            else
                missingSkills.Add(req.Text);
        }

        // Stage 3: ask the model ONLY for the parts that genuinely need reasoning —
        // summing years of experience, scoring, and writing suggestions — using the
        // already-confirmed skill lists as fixed context it must not override.
        return await FinalizeAsync(resume, experienceRequirements, matchingSkills, missingSkills);
    }

    // Stage 1: extract requirements from the job description ONLY, each tagged with
    // a type so we know HOW to check it later (deterministic keyword match vs.
    // numeric/date reasoning). Vague, subjective requirements (team spirit, etc.)
    // are dropped here because no resume text can objectively confirm or deny them.
    private async Task<List<RequirementItem>?> ExtractRequirementsAsync(string jobDesc)
    {
        var url = configuration["Ollama:Url"] ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "gemma4:12b";

        var prompt = $@"Extract job requirements from the text below that can be OBJECTIVELY verified from a resume. For each one, classify it as exactly one of:
- ""skill"": a specific technology, tool, framework, language, or technical skill (e.g. ""C#"", ""SQL Server"", ""ASP.NET"", ""Design Patterns"").
- ""experience"": a required MINIMUM number of years of experience (a numeric threshold).

Do NOT include vague soft-skill, personality, or attitude requirements that cannot be reliably checked from resume text (e.g. ""team spirit"", ""problem-solving ability"", ""motivation"", ""creativity"", ""responsibility""). Exclude these entirely — do not output them as either type.

IMPORTANT — keep every requirement ATOMIC: one single technology/skill per item. If a sentence lists several technologies together (e.g. ""C# and .NET"", ""SQL Server and Entity Framework"", or the same thing joined by ""و"" in Persian), split it into SEPARATE items — one per technology — instead of returning the whole sentence as one item. Each item's text should ideally be just the technology name itself (e.g. ""C#"", "".NET""), not a full sentence around it.

The job description may be written in Persian, English, or a mix — read and fully understand it before extracting, regardless of language. Write each requirement's ""text"" as the technology/skill name itself — prefer the common English name for well-known technologies (e.g. "".NET"", ""C#"") even if the surrounding job description sentence was in Persian, since that name is what will be searched for in the resume.

Job Description:
{jobDesc}

Return ONLY this JSON, no explanation, no markdown fences:
{{
  ""requirements"": [
    {{ ""text"": ""<requirement copied or closely paraphrased from the text above>"", ""type"": ""skill"" }},
    {{ ""text"": ""<minimum years requirement, exactly as stated>"", ""type"": ""experience"" }}
  ]
}}";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0 }
        };

        var response = await httpClient.PostAsJsonAsync(url, payload);
        response.EnsureSuccessStatusCode();

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>();
        if (string.IsNullOrWhiteSpace(ollamaResponse?.Response)) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<RequirementsExtraction>(ollamaResponse.Response, options);
            return raw?.Requirements;
        }
        catch
        {
            return null;
        }
    }

    // Deterministic "does this skill appear in the resume" check. Skill descriptors from
    // the job description often include parenthetical examples or slashes (e.g.
    // "API Gateway (Ocelot / YARP)"), so we split into individual candidate tokens and
    // match ANY of them case-insensitively rather than requiring the full literal phrase.
    private static bool ResumeContainsSkill(string resume, string skillText)
    {
        foreach (var token in ExtractCandidateTokens(skillText))
        {
            if (token.Length > 1 && resume.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static List<string> ExtractCandidateTokens(string skillText)
    {
        var parts = skillText
            .Split(new[] { '(', ')', '/', ',', '&' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => System.Text.RegularExpressions.Regex.Split(p, @"\s+and\s+|\s+و\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Select(p => p.Trim())
            .Where(p => p.Length > 1)
            .ToList();

        // Defense-in-depth: also pull out Latin-script technology-looking tokens
        // (e.g. "C#", ".NET", "ASP.NET") embedded inside a longer non-Latin sentence,
        // in case the model still returns a compound phrase despite the instruction above.
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(skillText, @"[A-Za-z][A-Za-z0-9.#+\-]{1,30}"))
        {
            if (m.Value.Length > 1) parts.Add(m.Value.Trim());
        }

        parts.Add(skillText.Trim());
        return parts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Stage 3: the model only reasons about years-of-experience thresholds (real
    // reasoning over dates) and writes the score/suggestions. It is given the
    // already-confirmed skill lists as fixed facts, not something to re-derive.
    private async Task<AnalyzeResult?> FinalizeAsync(
        string resume,
        List<RequirementItem> experienceRequirements,
        List<string> matchingSkills,
        List<string> missingSkills)
    {
        var url = configuration["Ollama:Url"] ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "llama3.2";

        var expList = experienceRequirements.Count > 0
            ? string.Join("\n", experienceRequirements.Select(r => "- " + r.Text))
            : "(none)";

        var prompt = $@"You are finishing an ATS analysis. The skill-matching part is already done by deterministic code — treat it as fact, do not re-evaluate or contradict it.

Already confirmed as present in the resume:
{(matchingSkills.Count > 0 ? string.Join(", ", matchingSkills) : "(none)")}

Already confirmed as absent from the resume:
{(missingSkills.Count > 0 ? string.Join(", ", missingSkills) : "(none)")}

Experience requirements still to evaluate (minimum years thresholds):
{expList}

Resume (use this ONLY to evaluate the experience requirements above — the skill lists above are already final):
{resume}

For each experience requirement: look at ALL job entries in the resume's work history, calculate the duration of each from its start/end dates (e.g. ""Jul 2016 - Jul 2021"" = 5 years), and SUM them to get total professional experience. If the total meets or exceeds the required number, it is met; otherwise it is not met.

Return ONLY this JSON, no explanation, no markdown fences:
{{
  ""atsScore"": <number 0-100, reflecting the overall match using both the confirmed skill lists and the experience evaluation>,
  ""experienceMet"": [""<experience requirement text that IS satisfied>""],
  ""experienceNotMet"": [""<experience requirement text that is NOT satisfied>""],
  ""criticalMissingSkills"": [""<subset of the confirmed-absent list above, or unmet experience requirements, that are mandatory (not ""nice to have"")>""],
  ""suggestions"": [""<2-4 concrete improvements based only on the confirmed-absent list and any unmet experience requirements>""]
}}";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0 }
        };

        var response = await httpClient.PostAsJsonAsync(url, payload);
        response.EnsureSuccessStatusCode();

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>();
        if (string.IsNullOrWhiteSpace(ollamaResponse?.Response)) return null;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<FinalizeResult>(ollamaResponse.Response, options);
        if (raw is null) return null;

        // Merge deterministic skill results with the model's experience-requirement
        // verdicts in C#, so the final lists can never be silently altered by the model.
        var finalMatching = matchingSkills.Concat(raw.ExperienceMet).ToList();
        var finalMissing = missingSkills.Concat(raw.ExperienceNotMet).ToList();

        return new AnalyzeResult(
            raw.AtsScore,
            finalMatching,
            finalMissing,
            raw.CriticalMissingSkills,
            raw.Suggestions
        );
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }

    private class RequirementItem
    {
        public string Text { get; set; } = string.Empty;
        public string Type { get; set; } = "skill"; // "skill" | "experience"
    }

    private class RequirementsExtraction
    {
        public List<RequirementItem> Requirements { get; set; } = new();
    }

    private class FinalizeResult
    {
        public int AtsScore { get; set; }
        public List<string> ExperienceMet { get; set; } = new();
        public List<string> ExperienceNotMet { get; set; } = new();
        public List<string> CriticalMissingSkills { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
    }
}