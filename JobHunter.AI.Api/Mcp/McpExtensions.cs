using System.Net;
using System.Security.Claims;
using JobHunter.AI.Api.Security;
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
        ValidateCredentials(builder.Configuration);
        // Validate configuration at startup; the DI-owned singleton is shared by every MCP call.
        using (var validation = new McpResourceLimits(builder.Configuration)) { }
        builder.Services.AddSingleton<McpResourceLimits>();
        builder.Services.AddAuthorization();
        // Pin the SDK 2.4.0 stateless HTTP mode: no long-lived MCP sessions can carry privileges.
        builder.Services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .AddAuthorizationFilters()
            .WithToolsFromAssembly(serializerOptions: CreateToolJsonOptions());
    }

    internal static void ValidateCredentials(IConfiguration configuration)
    {
        var readToken = configuration["Mcp:Token"];
        if (string.IsNullOrWhiteSpace(readToken) || readToken.Length < 32)
            throw new InvalidOperationException("MCP requires a randomly generated token of at least 32 characters.");

        var writeToken = configuration["Mcp:WriteToken"];
        if (!string.IsNullOrEmpty(writeToken) &&
            (!RestAccess.IsValidToken(writeToken) ||
             string.Equals(writeToken, readToken, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(writeToken, configuration["RestAuth:Token"], StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Mcp:WriteToken must be a separate 64-character hexadecimal credential, distinct from the MCP read and REST tokens.");

        if (configuration.GetValue<bool>("Mcp:AllowWrites") && string.IsNullOrEmpty(writeToken))
            throw new InvalidOperationException("Mcp:AllowWrites requires a separate Mcp:WriteToken.");
    }

    internal static JsonSerializerOptions CreateToolJsonOptions() => new(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Arabic,
            UnicodeRanges.ArabicSupplement, UnicodeRanges.ArabicExtendedA)
    };

    public static void UseJobHunterMcp(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Mcp:Enabled")) return;
        var limits = app.Services.GetRequiredService<McpResourceLimits>();
        var expectedRead = SHA256.HashData(Encoding.UTF8.GetBytes(app.Configuration["Mcp:Token"]!));
        var configuredWriteToken = app.Configuration["Mcp:WriteToken"];
        var expectedWrite = string.IsNullOrEmpty(configuredWriteToken) ? null :
            SHA256.HashData(Encoding.UTF8.GetBytes(configuredWriteToken));
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/mcp")) { await next(context); return; }
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
            using var rate = limits.TryRequest();
            if (!rate.IsAcquired)
            {
                RejectRateLimit(context, 60);
                return;
            }
            var authorization = context.Request.Headers.Authorization;
            var auth = authorization.ToString();
            if (authorization.Count != 1 || auth.Length > 4096 ||
                !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 401;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(auth[7..]));
            var isRead = CryptographicOperations.FixedTimeEquals(expectedRead, actual);
            var isWrite = expectedWrite is not null && CryptographicOperations.FixedTimeEquals(expectedWrite, actual);
            if (!isRead && !isWrite)
            {
                context.Response.StatusCode = 401;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }
            var role = isWrite ? "McpWriter" : "McpReader";
            // NameIdentifier also binds SDK stateful sessions to this credential class if enabled later.
            context.User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, isWrite ? "jobhunter-mcp-writer" : "jobhunter-mcp-reader"),
                new Claim(ClaimTypes.Role, role)
            ], authenticationType: "McpBearer"));
            using var transport = limits.TryTransport();
            if (!transport.IsAcquired)
            {
                RejectRateLimit(context, 1);
                return;
            }
            if (!HttpMethods.IsPost(context.Request.Method)) { await next(context); return; }
            await InvokeBoundedPostAsync(context, next, limits.MaxBodyBytes);
        });
    }

    private static void RejectRateLimit(HttpContext context, int retrySeconds)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retrySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // Bound actual bytes, including chunked requests. Do not rely solely on Content-Length.
    // Leave JSON-RPC parsing and protocol errors to the SDK.
    private static async Task InvokeBoundedPostAsync(HttpContext context, RequestDelegate next, int maxBytes)
    {
        var request = context.Request;
        if (request.ContentLength > maxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        if (request.Headers.TryGetValue("Content-Encoding", out var encoding) &&
            !string.Equals(encoding.ToString(), "identity", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        var original = request.Body;
        try
        {
            while (true)
            {
                var count = await original.ReadAsync(buffer.AsMemory(0,
                    Math.Min(buffer.Length, maxBytes - (int)body.Length + 1)), deadline.Token);
                if (count == 0) break;
                if (body.Length + count > maxBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                body.Write(buffer, 0, count);
            }
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
            return;
        }
        body.Position = 0;
        request.Body = body;
        try { await next(context); }
        finally { request.Body = original; }
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
