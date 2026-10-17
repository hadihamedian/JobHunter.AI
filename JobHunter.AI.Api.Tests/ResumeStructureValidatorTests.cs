using JobHunter.AI.Api.Services;
using Xunit;

namespace JobHunter.AI.Api.Tests;

public class ResumeStructureValidatorTests
{
    [Fact]
    public void Compare_WhenIdentical_ReturnsNoViolations()
    {
        var markdown = """
                       # John Doe
                       **Senior Engineer**

                       ## Summary
                       Seasoned software engineer.

                       ## Work Experience
                       ### Tech Lead at Acme
                       *2020 - Present*
                       - Led migration
                       - Reduced latency

                       ## Skills
                       **Languages:** C#, SQL

                       ## Selected Projects
                       ### Core Engine
                       Backend service.
                       `C#` · `Redis` · `Kafka`
                       """;

        var orig = ResumeStructureValidator.Extract(markdown);
        var tailored = ResumeStructureValidator.Extract(markdown);

        var violations = ResumeStructureValidator.Compare(orig, tailored);
        Assert.Empty(violations);
    }

    [Fact]
    public void Compare_WhenBulletDropped_ReportsViolation()
    {
        var origMd = """
                     ## Work Experience
                     ### Tech Lead at Acme
                     - Bullet 1
                     - Bullet 2
                     """;

        var tailoredMd = """
                         ## Work Experience
                         ### Tech Lead at Acme
                         - Bullet 1 only
                         """;

        var orig = ResumeStructureValidator.Extract(origMd);
        var tailored = ResumeStructureValidator.Extract(tailoredMd);

        var violations = ResumeStructureValidator.Compare(orig, tailored);
        Assert.Contains(violations, v => v.Contains("had 2 bullets"));
    }

    [Fact]
    public void DetectDuplicateHeadings_WhenRepeated_DetectsViolation()
    {
        var md = "## Summary\nSummary\nExperienced developer.";
        var violations = ResumeStructureValidator.DetectDuplicateHeadings(md);
        Assert.NotEmpty(violations);
    }
}