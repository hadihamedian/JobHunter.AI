using JobHunter.AI.Api.Skills;
using Xunit;

namespace JobHunter.AI.Api.Tests;

public class AtsEvaluationSkillsTests
{
    private readonly AtsEvaluationSkills _skills = new();

    [Theory]
    // Valid positives
    [InlineData("Proficient in C# and .NET 10", "C#", true)]
    [InlineData("Deep experience with .NET Core backend", ".NET", true)]
    [InlineData("Backend developer using Node.js and TypeScript", "Node.js", true)]
    [InlineData("Modern C++20 developer", "C++", true)]
    [InlineData("Managed SQL Server instances", "SQL Server", true)]
    // Aliases
    [InlineData("Hands-on with csharp programming", "C#", true)]
    [InlineData("Deployed dotnet services", ".NET", true)]
    [InlineData("Used Postgres for high throughput", "PostgreSQL", true)]
    [InlineData("Modern JS web development", "JavaScript", true)]
    [InlineData("Built services using EF Core", "Entity Framework", true)]
    // Critical Negatives (False positive prevention)
    [InlineData("Senior JavaScript Developer", "Java", false)]
    [InlineData("Experienced with NoSQL and MySQL databases", "SQL", false)]
    [InlineData("Worked at Google on MongoDB systems", "Go", false)]
    [InlineData("Extensive DevOps and CI/CD pipelines", "Azure DevOps", false)]
    // Persian separators
    [InlineData("مسلط به سی‌شارپ و PostgreSQL", "PostgreSQL و Docker", true)]
    public void CheckSkillPresence_TableDriven_EvaluatesCorrectly(string resume, string skill, bool expected)
    {
        var result = _skills.CheckSkillPresence(resume, skill);
        Assert.Equal(expected, result);
    }
}