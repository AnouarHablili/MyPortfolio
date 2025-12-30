# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY *.sln ./
COPY src/MyPortfolio.Core/MyPortfolio.Core.csproj src/MyPortfolio.Core/
COPY src/MyPortfolio.Shared/MyPortfolio.Shared.csproj src/MyPortfolio.Shared/
COPY src/MyPortfolio.Web/MyPortfolio.Web.csproj src/MyPortfolio.Web/

# Restore dependencies
RUN dotnet restore src/MyPortfolio.Web/MyPortfolio.Web.csproj

# Copy the rest of the source code
COPY . .

# Build and publish the application
WORKDIR /src/src/MyPortfolio.Web
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos '' appuser

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Expose port
EXPOSE 8080

# Switch to non-root user
USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "MyPortfolio.Web.dll"]
