using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using JobHunter.AI.Web;
using JobHunter.AI.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = builder.HostEnvironment.IsDevelopment()
    ? "http://localhost:5065"
    : "";

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = string.IsNullOrEmpty(baseAddress)
        ? new Uri(builder.HostEnvironment.BaseAddress)
        : new Uri(baseAddress),
    Timeout = TimeSpan.FromMinutes(5)
});

builder.Services.AddScoped<ApplicationService>();

builder.Services.AddScoped<ResumeService>();
builder.Services.AddScoped<InterviewService>();

await builder.Build().RunAsync();