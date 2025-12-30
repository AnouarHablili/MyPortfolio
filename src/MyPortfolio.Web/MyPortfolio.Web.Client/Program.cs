using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MyPortfolio.Shared.Services;
using MyPortfolio.Shared.ViewModels;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Register HttpClient with base address for API calls
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

// Register Shared Services
builder.Services.AddScoped<POCService>();
builder.Services.AddScoped<AccessCodeService>();
builder.Services.AddScoped<NavigationService>();
builder.Services.AddScoped<PrioritizerViewModel>();
builder.Services.AddSingleton<ExportService>();

await builder.Build().RunAsync();
