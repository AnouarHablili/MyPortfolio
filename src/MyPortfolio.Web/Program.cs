using MyPortfolio.Core.Extensions;
using MyPortfolio.Core.Features.RAG.Extensions;
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
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Configure listening URLs based on environment
if (!builder.Environment.IsDevelopment())
{
    // Production: Fly.io expects HTTP on port 8080 internally (handles HTTPS at edge)
    builder.WebHost.UseUrls("http://0.0.0.0:8080");
}
else
{
    // Development: Use HTTP only - simpler and avoids certificate issues
    // The HttpClient will also use HTTP in development
    builder.WebHost.UseUrls("http://localhost:5191");
}

// Configure forwarded headers for Fly.io proxy (needed for WebSocket support)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

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
    if (builder.Environment.IsDevelopment())
    {
        // Development: Use HTTP for local API calls to match server configuration
        client.BaseAddress = new Uri("http://localhost:5191/");
    }
    else
    {
        // Production: Use HTTP localhost since we're calling our own API from within the container
        // Fly.io handles HTTPS at the edge/proxy level
        client.BaseAddress = new Uri("http://localhost:8080/");
    }
});

// Register a factory-based HttpClient for AccessCodeService
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("LocalApi");
});

// Register All AI Services (Gemini, OpenAI, Azure OpenAI, Anthropic, Cohere)
// Only Gemini is currently configured with an API key - others are ready for future use
builder.Services.AddAIServices(builder.Configuration);

// Register RAG Services (Session management, chunking, embedding, vector store, strategies)
builder.Services.AddRAGServices(builder.Configuration);

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
builder.Services.AddScoped<RAGViewModel>();
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

// Use forwarded headers (must be before other middleware)
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");

// WebSocket support for Blazor Server SignalR
app.UseWebSockets();

// Only use HTTPS redirection in development - Fly.io handles HTTPS at the edge
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

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
app.MapRAGEndpoints();

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
