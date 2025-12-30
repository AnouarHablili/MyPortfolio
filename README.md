# AiShowCase

## POC Showcase

A streamlined Blazor Web App (.NET 10) for showcasing Proof-of-Concept projects as interactive, clickable cards.

> **?? For detailed technical decisions and implementation details, see [TECHNICAL-DECISIONS-CONSOLIDATED.md](TECHNICAL-DECISIONS-CONSOLIDATED.md)**

### Overview

This application provides a clean interface for displaying POCs (Proof-of-Concepts) with:

- **Open browsing**: View all POC cards and pages without authentication
- **Protected interactions**: Access code required only for AI-powered features and submit buttons
- Clickable POC cards with status indicators (Active, Coming Soon, Deprecated)
- Simple navigation and user experience
- Integration with AI services (Google Gemini) for POC features

## Access Control Philosophy

? **Browse freely**: All content, POC descriptions, and pages are publicly accessible

?? **Unlock to interact**: Access code required only for:
- Submit buttons in POCs
- AI-powered features (e.g., Task Prioritizer)
- API calls that incur costs

This approach allows you to showcase your work openly while protecting against API cost abuse.

### Project Structure

```
MyPortfolio/
??? src/
?   ??? MyPortfolio.Core/              # Core business logic & AI integrations
?   ??? MyPortfolio.Shared/            # Shared components, models, and services
?   ??? MyPortfolio.Web/
?       ??? MyPortfolio.Web/           # Server-side Blazor application
??? tests/
    ??? MyPortfolio.Core.Tests/        # Unit tests
```

### Features

- **POC Gallery**: Display POCs as interactive cards (no auth required)
- **Protected Actions**: Submit buttons disabled until access code provided
- **AI Integration**: Task prioritization using Google Gemini 2.5 Flash
- **Responsive Design**: Mobile-friendly interface
- **JWT Authentication**: Secure token-based access control
- **Rate Limiting**: Protection against brute force attacks
- **Export Functionality**: Export prioritization results as JSON or Text
- **Health Checks**: Monitor application health
- **OpenAPI/Swagger**: API documentation (Development only)
- **Structured Logging**: Comprehensive logging throughout the application
- **Request/Response Logging**: Middleware for tracking HTTP requests

### Technology Stack

- .NET 10
- Blazor Server (Interactive)
- Google Gemini API (Gemini 2.5 Flash)
- Bootstrap 5
- JWT Bearer Authentication
- ASP.NET Core Health Checks
- OpenAPI/Swagger

## Quick Start

### Prerequisites

- .NET 10 SDK
- Google Gemini API Key ([Get one here](https://makersuite.google.com/app/apikey))

### Local Development

1. Clone the repository:
   ```bash
   git clone https://github.com/AnouarHablili/MyPortfolio.git
   cd MyPortfolio
   ```

2. Set up user secrets for sensitive configuration:
   ```bash
   cd src/MyPortfolio.Web/MyPortfolio.Web
   dotnet user-secrets set "Gemini:ApiKey" "your-actual-gemini-api-key"
   dotnet user-secrets set "AccessCode:Code" "your-access-code"
   ```

3. Run the application:
   ```bash
   dotnet run
   ```

4. Open your browser and navigate to `https://localhost:5001`
5. Browse POCs freely, click "Unlock Features" in the nav menu when you want to interact
6. Enter the access code you configured (default in Development: `demo2025`)

## Configuration

### Required Configuration Values

| Key | Description | Default (Dev) |
|-----|-------------|---------------|
| `Gemini:ApiKey` | Google Gemini API Key | (user secret) |
| `AccessCode:Code` | Access code to unlock features | `demo2025` |

### Optional Configuration

| Key | Description | Default |
|-----|-------------|---------|
| `Gemini:MaxRetries` | Maximum retry attempts for API calls | `3` |
| `Gemini:InitialRetryDelaySeconds` | Initial delay before retry | `1` |
| `Gemini:RequestTimeoutSeconds` | Request timeout in seconds | `60` |
| `AccessCode:TokenExpirationHours` | Hours before token expires | `24` |
| `AccessCode:MaxValidationAttempts` | Max validation attempts per IP | `5` |
| `AccessCode:RateLimitWindowMinutes` | Rate limit window | `15` |

?? **Security Note**: Never commit real API keys or access codes to GitHub. Always use:
- **User Secrets** for local development
- **Azure Key Vault** or **Environment Variables** for production

## API Endpoints

### Access Code
- `POST /api/access-code/validate` - Validate access code and receive JWT token
- `POST /api/access-code/refresh` - Refresh an existing JWT token

### Prioritizer
- `POST /api/prioritizer/prioritize` - AI-powered task prioritization (requires JWT)

### Health Checks
- `GET /health` - Overall application health
- `GET /health/ready` - Readiness probe
- `GET /health/live` - Liveness probe

### OpenAPI Documentation
- `GET /openapi/v1.json` - OpenAPI specification (Development only)

## Testing

Run unit tests with:

```bash
dotnet test
```

Test coverage includes:
- GeminiService (AI integration)
- AccessCodeService (authentication)
- PrioritizerViewModel (UI logic)

## Architecture Highlights

### Clean Architecture
- **Core**: Business logic & AI services (framework-independent)
- **Shared**: UI components, view models, and services
- **Web**: Infrastructure, API endpoints, and hosting

### Key Patterns
- **Dependency Injection**: IoC throughout the application
- **Result Pattern**: Clean error handling without exceptions
- **Repository Pattern**: Abstract AI service implementation
- **Minimal APIs**: Modern endpoint design

### Performance
- **Server-Side Rendering**: 10x faster initial load vs WebAssembly
- **SignalR**: Real-time updates with minimal overhead
- **Optimized Bundle**: ~200 KB initial download

## Security Features

- **JWT Token Authentication**: Secure token-based access with expiration
- **Bearer Tokens**: Industry-standard authorization
- **Rate Limiting**: IP-based rate limiting for access code validation
- **Access Code Protection**: Cost control for AI API usage
- **Request Logging**: Comprehensive logging of all HTTP requests

## Development Philosophy

This portfolio serves as:
- **Skills Showcase**: Demonstrates .NET 10 and Blazor capabilities
- **Best Practices**: Modern patterns, clean code, proper testing
- **Production Awareness**: Understands production requirements without over-engineering POC

## Documentation

- **[TECHNICAL-DECISIONS-CONSOLIDATED.md](TECHNICAL-DECISIONS-CONSOLIDATED.md)** - Complete technical journey and decisions
- **[README.md](README.md)** - This file (quick start and overview)

## License

Copyright (c) 2025 Anouar Hablili. All rights reserved.

MIT License - See [LICENSE](LICENSE) for details.
