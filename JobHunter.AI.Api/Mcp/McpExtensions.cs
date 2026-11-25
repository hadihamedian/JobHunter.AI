using System.Net;
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
        builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();
    }

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
                || context.Request.Headers.ContainsKey("Origin"))
            {
                context.Response.StatusCode = 403;
                return;
            }
            var auth = context.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
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

    private static bool IsLocalHost(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
}
