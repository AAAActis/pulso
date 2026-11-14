# Multi-stage Dockerfile for Pulso API
# Target: Alpine Linux, amd64, non-root user (1654), no SDK in final image

# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy project files for layer caching
COPY src/Pulso.Api/Pulso.Api.csproj ./Pulso.Api/
COPY tests/Pulso.Tests/Pulso.Tests.csproj ./Pulso.Tests/
COPY Pulso.slnx ./

# Restore dependencies
RUN dotnet restore Pulso.slnx

# Copy source and build
COPY src/Pulso.Api/ ./Pulso.Api/
COPY tests/Pulso.Tests/ ./Pulso.Tests/

RUN dotnet build Pulso.slnx -c Release --no-restore

# Run tests in build stage (fails build if tests fail)
RUN dotnet test Pulso.slnx -c Release --no-build --no-restore --verbosity normal

# Publish app
RUN dotnet publish Pulso.Api/Pulso.Api.csproj -c Release -o /app/publish --no-restore \
    -p:PublishTrimmed=false \
    -p:InvariantGlobalization=true \
    -p:DockerDefaultTargetOS=Linux

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Install required runtime dependencies (curl for healthchecks, tzdata for timezone)
RUN apk add --no-cache curl tzdata

# Create non-root user (UID 1654 = standard non-root in many distros)
RUN adduser -D -u 1654 -s /sbin/nologin appuser

# Copy published app from build stage
COPY --from=build --chown=appuser:appuser /app/publish ./

# Create data directory for SQLite (will be mounted as volume)
RUN mkdir -p /data && chown appuser:appuser /data

# Switch to non-root user
USER 1654

# Environment
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    TZ=UTC

# Expose port
EXPOSE 8080

# Health check (uses /health/ready endpoint)
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health/ready || exit 1

# Entry point
ENTRYPOINT ["dotnet", "Pulso.Api.dll"]