using MyPortfolio.Core.Extensions;
using MyPortfolio.Shared.Configuration;
using MyPortfolio.Shared.Services;
using MyPortfolio.Shared.ViewModels;
using MyPortfolio.Web.Components;
using MyPortfolio.Web.Endpoints;
using MyPortfolio.Web.Middleware;
using MyPortfolio.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container - Server-side only
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register Configuration Options
builder.Services.Configure<AccessCodeOptions>(
    builder.Configuration.GetSection(AccessCodeOptions.SectionName));

// Configure JWT Authentication
var accessCodeOptions = builder.Configuration.GetSection(AccessCodeOptions.SectionName).Get<AccessCodeOptions>();
if (accessCodeOptions != null)
{
    // Generate the same signing key used in JwtTokenService
    var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(accessCodeOptions.Code + "JWT_SIGNING_KEY"));
    var signingKey = new SymmetricSecurityKey(keyBytes);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

    builder.Services.AddAuthorization();
}

// Register HttpClient for server-side services
builder.Services.AddHttpClient("LocalApi", (sp, client) =>
{
    // For server-side Blazor, we can use relative URLs or determine the base URL from the request context
    // In production, we'll use the app's own base URL
    if (builder.Environment.IsDevelopment())
    {
        client.BaseAddress = new Uri("https://localhost:7024/");
    }
    else
    {
        // In production, use http://localhost:8080 since we're calling our own API from within the container
        client.BaseAddress = new Uri("http://localhost:8080/");
    }
});

// Register a factory-based HttpClient for AccessCodeService
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("LocalApi");
});

// Register Core Services (Gemini AI Service)
builder.Services.AddGeminiService(builder.Configuration);

// Register JWT Token Service
builder.Services.AddSingleton<JwtTokenService>();

// Register Rate Limiting Service
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RateLimitService>();

// Register Shared Services
builder.Services.AddScoped<POCService>();
builder.Services.AddScoped<AccessCodeService>();
builder.Services.AddScoped<NavigationService>();
builder.Services.AddScoped<PrioritizerViewModel>();
builder.Services.AddSingleton<ExportService>();

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Add Response Caching
builder.Services.AddResponseCaching();
builder.Services.AddMemoryCache();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

// Add response caching
app.UseResponseCaching();

// Add request/response logging middleware
app.UseMiddleware<RequestResponseLoggingMiddleware>();

// Add Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Map Health Check Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            })
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

// Map API Endpoints
app.MapAccessCodeEndpoints();
app.MapPrioritizerEndpoints();

// Map OpenAPI/Swagger
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapStaticAssets();

// Map Razor Components - Server-side only, no WebAssembly
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
