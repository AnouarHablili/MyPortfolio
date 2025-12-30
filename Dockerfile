# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY *.sln ./
COPY src/MyPortfolio.Core/MyPortfolio.Core.csproj src/MyPortfolio.Core/
COPY src/MyPortfolio.Shared/MyPortfolio.Shared.csproj src/MyPortfolio.Shared/
COPY src/MyPortfolio.Web/MyPortfolio.Web.csproj src/MyPortfolio.Web/

# Restore dependencies (fresh restore in container)
RUN dotnet restore src/MyPortfolio.Web/MyPortfolio.Web.csproj

# Copy the rest of the source code (excluding obj/bin via .dockerignore)
COPY src/MyPortfolio.Core/ src/MyPortfolio.Core/
COPY src/MyPortfolio.Shared/ src/MyPortfolio.Shared/
COPY src/MyPortfolio.Web/ src/MyPortfolio.Web/

# Build and publish the application (with restore to ensure clean build)
WORKDIR /src/src/MyPortfolio.Web
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Expose port
EXPOSE 8080

# Use the built-in non-root user (available in .NET 8+ images)
USER $APP_UID

# Entry point
ENTRYPOINT ["dotnet", "MyPortfolio.Web.dll"]
