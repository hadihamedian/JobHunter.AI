using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ModelContextProtocol;
using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.AspNetCore;

namespace JobHunter.AI.Api.Mcp;

public static class McpExtensions
{
    public static void AddJobHunterMcp(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("Mcp:Enabled")) return;
        var token = builder.Configuration["Mcp:Token"];
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new InvalidOperationException("MCP requires a randomly generated token of at least 32 characters.");
        builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly(serializerOptions: CreateToolJsonOptions());
    }

    internal static JsonSerializerOptions CreateToolJsonOptions() => new(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Arabic,
            UnicodeRanges.ArabicSupplement, UnicodeRanges.ArabicExtendedA)
    };

    public static void UseJobHunterMcp(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Mcp:Enabled")) return;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(app.Configuration["Mcp:Token"]!));
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/mcp")) { await next(); return; }
            // Local-only preview. Do not place behind a reverse proxy.
            var ip = context.Connection.RemoteIpAddress;
            if (ip is null || !IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip)
                || !IsLocalHost(context.Request.Host.Host)
                || HasUntrustedHeaders(context.Request.Headers.Keys))
            {
                context.Response.StatusCode = 403;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            var authorization = context.Request.Headers.Authorization;
            var auth = authorization.ToString();
            if (authorization.Count != 1 || auth.Length > 4096 || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(auth[7..]))))
            {
                context.Response.StatusCode = 401;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }
            await next();
        });
    }

    public static void MapJobHunterMcp(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("Mcp:Enabled")) app.MapMcp("/mcp");
    }

    internal static bool HasUntrustedHeaders(IEnumerable<string> names) => names.Any(k =>
        k.Equals("Origin", StringComparison.OrdinalIgnoreCase) ||
        k.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
        k.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase));

    private static bool IsLocalHost(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
}
