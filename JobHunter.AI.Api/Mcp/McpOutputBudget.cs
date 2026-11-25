using System.Text.Json;
using ModelContextProtocol;

namespace JobHunter.AI.Api.Mcp;

internal static class McpOutputBudget
{
    internal const int MaxPayloadBytes = 128 * 1024;
    private static readonly JsonSerializerOptions Options = McpExtensions.CreateToolJsonOptions();

    // Check the typed tool payload before the SDK creates its protocol envelope.
    // Reject rather than truncate: partial pages or ATS results could mislead clients.
    internal static T Check<T>(T value)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(value, Options).Length > MaxPayloadBytes)
            throw new McpException("MCP result exceeds the output budget. Reduce the page size or shorten the stored data.");
        return value;
    }
}
