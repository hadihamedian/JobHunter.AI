using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace JobHunter.AI.Api.Security;

public static class RestAccess
{
    public static void UseRestAccess(this WebApplication app)
    {
        var enabled = app.Configuration.GetValue("RestAuth:Enabled", true);
        if (!enabled && !app.Environment.IsDevelopment())
            throw new InvalidOperationException("Disabling REST authentication is only supported in Development.");

        var token = app.Configuration["RestAuth:Token"];
        if (!string.IsNullOrEmpty(token) && !IsValidToken(token))
            throw new InvalidOperationException("RestAuth:Token must be 64 hexadecimal characters. Generate a separate random token.");
        var mcpToken = app.Configuration["Mcp:Token"];
        if (!string.IsNullOrEmpty(token) && string.Equals(token, mcpToken, StringComparison.Ordinal))
            throw new InvalidOperationException("REST and MCP tokens must be different.");

        var expected = string.IsNullOrEmpty(token) ? null : Hash(token);
        app.Use(async (context, next) =>
        {
            // MCP has its own transport authentication; no REST credential is accepted there.
            if (context.Request.Path.StartsWithSegments("/mcp"))
            {
                await next();
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            if (!enabled)
            {
                var ip = context.Connection.RemoteIpAddress;
                var host = context.Request.Host.Host;
                if (ip is null || !IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip)
                    || !(host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                        || (IPAddress.TryParse(host.Trim('[', ']'), out var hostIp) && IPAddress.IsLoopback(hostIp)))
                    || context.Request.Headers.Keys.Any(k => k.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                        || k.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next();
                return;
            }
            if (expected is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            var headers = context.Request.Headers.Authorization;
            if (headers.Count != 1 || !Matches(expected, headers.ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }

            await next();
        });
    }

    internal static bool IsValidToken(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    internal static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    internal static bool Matches(byte[] expected, string authorization) =>
        authorization.Length == 71 &&
        authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
        IsValidToken(authorization[7..]) &&
        CryptographicOperations.FixedTimeEquals(expected, Hash(authorization[7..]));
}
