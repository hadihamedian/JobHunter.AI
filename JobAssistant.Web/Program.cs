using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using JobAssistant.Web;
using JobAssistant.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri("http://localhost:5065"),
    Timeout = TimeSpan.FromMinutes(5)
});

builder.Services.AddScoped<ApplicationService>();

builder.Services.AddScoped<ResumeService>();
builder.Services.AddScoped<InterviewService>();

await builder.Build().RunAsync();