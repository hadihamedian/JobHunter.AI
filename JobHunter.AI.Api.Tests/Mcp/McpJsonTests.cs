using System.Text.Json;
using JobHunter.AI.Api.Mcp;
using Xunit;

namespace JobHunter.AI.Api.Tests.Mcp;

public class McpJsonTests
{
    [Fact]
    public void PersianTextIsReadableAndRoundTrips()
    {
        const string value = "توسعه‌دهنده نرم افزار";
        var options = McpExtensions.CreateToolJsonOptions();
        var json = JsonSerializer.Serialize(new { CompanyName = value }, options);
        Assert.Contains("نرم افزار", json);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(value, parsed.RootElement.GetProperty("companyName").GetString());
    }

    [Fact]
    public void HtmlSensitiveCharactersRemainEscaped()
    {
        const string value = "<script>&\"";
        var json = JsonSerializer.Serialize(value, McpExtensions.CreateToolJsonOptions());
        Assert.DoesNotContain("<script>", json);
        Assert.Equal(value, JsonSerializer.Deserialize<string>(json));
    }
}
