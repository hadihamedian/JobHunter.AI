namespace JobHunter.AI.Api.Services;

public static class JsonResponseCleaner
{
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = text.Trim();

        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastBacktick = cleaned.LastIndexOf("```");
            if (firstNewline > 0 && lastBacktick > firstNewline)
            {
                cleaned = cleaned.Substring(firstNewline + 1, lastBacktick - firstNewline - 1).Trim();
            }
        }

        var openBrace = cleaned.IndexOfAny(['{', '[']);
        var closeBrace = cleaned.LastIndexOfAny(['}', ']']);
        if (openBrace >= 0 && closeBrace > openBrace)
        {
            cleaned = cleaned.Substring(openBrace, closeBrace - openBrace + 1);
        }

        return cleaned.Trim();
    }
}