using System.ComponentModel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public class InterviewBankSkills(InterviewRepository repository)
{
    [Description("Searches the interview question bank by topic, keyword, question type, or status.")]
    public async Task<List<InterviewQuestion>> SearchQuestionsAsync(
        [Description("Topic or keyword to search for (e.g., 'EF Core', 'SQL', 'Behavioral'). Leave empty for all questions.")] string? topic = null)
    {
        var all = await repository.GetAllAsync();
        if (string.IsNullOrWhiteSpace(topic))
            return all;

        var kw = topic.Trim();
        return all.Where(q =>
            q.Question.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            q.QuestionType.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            q.Status.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            (q.Hint is not null && q.Hint.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
            (q.PersonalNote is not null && q.PersonalNote.Contains(kw, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }
}