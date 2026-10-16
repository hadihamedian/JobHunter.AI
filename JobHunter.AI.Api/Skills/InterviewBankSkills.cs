using System.ComponentModel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public class InterviewBankSkills(InterviewRepository repository)
{
    [Description("Searches and filters saved interview questions by a specific topic, technology, or keyword (e.g., 'EF Core', 'Behavioral', 'SQL').")]
    public async Task<List<InterviewQuestion>> SearchQuestionsAsync(
        [Description("The topic or keyword to search for in questions, hints, or notes")] string keyword)
    {
        var all = await repository.GetAllAsync();
        if (string.IsNullOrWhiteSpace(keyword)) return all;

        return all.Where(q =>
            q.Question.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            (q.Hint != null && q.Hint.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
            (q.PersonalNote != null && q.PersonalNote.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
            q.QuestionType.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    [Description("Retrieves all interview questions stored across all job applications.")]
    public async Task<List<InterviewQuestion>> GetAllQuestionsAsync()
    {
        return await repository.GetAllAsync();
    }
}