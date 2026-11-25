using JobHunter.AI.Api.Mcp;

namespace JobHunter.AI.Api.Tests.Mcp;

public class McpSecurityTests
{
    [Theory]
    [InlineData("Origin", true)]
    [InlineData("Forwarded", true)]
    [InlineData("x-forwarded-for", true)]
    [InlineData("X-Forwarded-Host", true)]
    [InlineData("Authorization", false)]
    [InlineData("Accept", false)]
    public void RejectsBrowserAndProxyHeaders(string header, bool expected)
        => Assert.Equal(expected, McpExtensions.HasUntrustedHeaders([header]));
}
