namespace JobHunter.AI.Api.Services;

public record ResumeSignature(
    List<string> SectionNames,
    Dictionary<string, int> BulletsPerJob,
    Dictionary<string, int> TechItemsPerProject
);

public static class ResumeStructureValidator
{
    public static ResumeSignature Extract(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n");
        var lines = text.Split('\n');

        var sections = new List<string>();
        var bulletsPerJob = new Dictionary<string, int>();
        var techPerProject = new Dictionary<string, int>();

        string? currentSection = null;
        string? currentEntry = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("## "))
            {
                currentSection = line[3..].Trim();
                sections.Add(currentSection);
                currentEntry = null;
            }
            else if (line.StartsWith("### "))
            {
                currentEntry = line[4..].Trim();
                if (currentSection == "Work Experience")
                    bulletsPerJob[currentEntry] = 0;
                else if (currentSection == "Selected Projects")
                    techPerProject[currentEntry] = 0;
            }
            else if (currentEntry is not null)
            {
                var trimmed = line.TrimStart();
                if (currentSection == "Work Experience" && trimmed.StartsWith("- "))
                {
                    bulletsPerJob[currentEntry]++;
                }
                else if (currentSection == "Selected Projects" && trimmed.Contains('·'))
                {
                    var items = trimmed.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    techPerProject[currentEntry] = items.Length;
                }
            }
        }

        return new ResumeSignature(sections, bulletsPerJob, techPerProject);
    }

    // چک جدید: بعضی وقتا مدل بعد از "## Summary" دوباره کلمه‌ی "Summary" رو به‌عنوان
    // یه خط جدا تکرار می‌کنه (مثلاً "## Summary\nSummary\nAs a seasoned..."). این متد
    // اون رو تشخیص می‌ده تا بره تو لیست violation ها و باعث retry بشه.
    public static List<string> DetectDuplicateHeadings(string markdown)
    {
        var violations = new List<string>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (!lines[i].StartsWith("## ")) continue;

            var sectionName = lines[i][3..].Trim();
            var nextNonEmpty = lines.Skip(i + 1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

            if (nextNonEmpty is not null &&
                string.Equals(nextNonEmpty.Trim(), sectionName, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(
                    $"The heading \"## {sectionName}\" is immediately followed by the word \"{nextNonEmpty.Trim()}\" " +
                    "on its own line — remove that duplicate line, it must not repeat the section name as plain text.");
            }
        }

        return violations;
    }

    public static List<string> Compare(ResumeSignature original, ResumeSignature tailored)
    {
        var violations = new List<string>();

        foreach (var section in original.SectionNames)
        {
            if (!tailored.SectionNames.Contains(section))
                violations.Add($"Missing entire section: \"## {section}\" — it must be present.");
        }

        foreach (var (jobTitle, originalCount) in original.BulletsPerJob)
        {
            var match = FindClosestKey(tailored.BulletsPerJob, jobTitle);
            var tailoredCount = match is null ? 0 : tailored.BulletsPerJob[match];
            if (tailoredCount < originalCount)
            {
                violations.Add(
                    $"Job \"{jobTitle}\" had {originalCount} bullets in the original but only {tailoredCount} " +
                    $"in your output — restore the missing bullet(s), rewording is fine but do not delete them.");
            }
        }

        foreach (var (projectTitle, originalCount) in original.TechItemsPerProject)
        {
            var match = FindClosestKey(tailored.TechItemsPerProject, projectTitle);
            var tailoredCount = match is null ? 0 : tailored.TechItemsPerProject[match];
            if (tailoredCount < originalCount)
            {
                violations.Add(
                    $"Project \"{projectTitle}\" had {originalCount} tech-stack items in the original but only " +
                    $"{tailoredCount} in your output — restore all missing technology names in the '·' separated line.");
            }
        }

        return violations;
    }

    private static string? FindClosestKey(Dictionary<string, int> dict, string originalTitle)
    {
        if (dict.ContainsKey(originalTitle)) return originalTitle;

        var firstToken = originalTitle.Split(' ', '—', '-').FirstOrDefault(t => t.Length > 2);
        if (firstToken is null) return null;

        return dict.Keys.FirstOrDefault(k => k.Contains(firstToken, StringComparison.OrdinalIgnoreCase));
    }
}