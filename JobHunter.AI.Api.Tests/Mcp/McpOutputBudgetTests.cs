using JobHunter.AI.Api.Mcp;
using ModelContextProtocol;

namespace JobHunter.AI.Api.Tests.Mcp;

public class McpOutputBudgetTests
{
    [Fact]
    public void SmallPayloadIsReturnedUnchanged()
    {
        var value = new[] { "سلام", "C#" };
        Assert.Same(value, McpOutputBudget.Check(value));
    }

    [Theory]
    [InlineData('x')]
    [InlineData('س')]
    public void OversizedPayloadIsRejectedWithoutEchoingData(char character)
    {
        var value = new string(character, McpOutputBudget.MaxPayloadBytes);
        var error = Assert.Throws<McpException>(() => McpOutputBudget.Check(value));
        Assert.Contains("output budget", error.Message);
        Assert.True(error.Message.Length < 200);
    }
}
