using System.Net;
using JobHunter.AI.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.AI.Api.Tests.Mcp;

public class RestAccessTests
{
    private static readonly string Token = new('a', 64);

    [Theory]
    [InlineData(null, 503)]
    [InlineData("", 503)]
    [InlineData("missing", 401)]
    [InlineData("wrong", 401)]
    [InlineData("correct", 204)]
    public async Task RestRequestsAreProtected(string? mode, int expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Integration", Args = []
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RestAuth:Token"] = string.IsNullOrEmpty(mode) ? "" : Token,
            ["Mcp:Token"] = ""
        });
        await using var app = builder.Build();
        app.UseRestAccess();
        app.MapGet("/api/auth/session", () => Microsoft.AspNetCore.Http.Results.NoContent());
        app.MapPost("/semantic-search", () => Microsoft.AspNetCore.Http.Results.NoContent());
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        if (mode is "correct" or "wrong")
            client.DefaultRequestHeaders.Authorization = new("Bearer", mode == "correct" ? Token : new string('b', 64));
        using var response = await client.GetAsync("/api/auth/session");
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        using var search = await client.PostAsync("/semantic-search", null);
        Assert.Equal((HttpStatusCode)expected, search.StatusCode);
        using var mcp = await client.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.NotFound, mcp.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task DevelopmentBypassAllowsLocalRequestsOnly()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RestAuth:Enabled"] = "false", ["RestAuth:Token"] = "", ["Mcp:Token"] = ""
        });
        await using var app = builder.Build();
        app.UseRestAccess();
        app.MapGet("/api/auth/session", () => Microsoft.AspNetCore.Http.Results.NoContent());
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var local = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.NoContent, local.StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
        foreign.Headers.Host = "example.invalid";
        using var rejected = await client.SendAsync(foreign);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
        forwarded.Headers.Add("X-Forwarded-For", "192.0.2.1");
        using var proxy = await client.SendAsync(forwarded);
        Assert.Equal(HttpStatusCode.Forbidden, proxy.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task ProductionCannotDisableRestAuthentication()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", Args = [] });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["RestAuth:Enabled"] = "false" });
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.UseRestAccess());
    }

    [Fact]
    public async Task InvalidOrSharedCredentialsFailAtStartup()
    {
        foreach (var token in new[] { "invalid", Token })
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Integration", Args = [] });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RestAuth:Token"] = token, ["Mcp:Token"] = Token
            });
            await using var app = builder.Build();
            Assert.Throws<InvalidOperationException>(() => app.UseRestAccess());
        }
    }
}
