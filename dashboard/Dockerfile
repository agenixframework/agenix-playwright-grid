# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore using only the project file for better layer caching
COPY Dashboard.csproj ./
RUN dotnet restore ./Dashboard.csproj

# Build and publish
COPY . .
RUN dotnet publish ./Dashboard.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS final
WORKDIR /app

# ✅ Install just curl for the healthcheck (small)
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl; \
    rm -rf /var/lib/apt/lists/*

# Container-friendly defaults and port
ENV ASPNETCORE_URLS=http://+:3001 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# Copy published output
COPY --from=build /app/publish .

# Run as a non-root user
RUN useradd -u 10001 -r -s /usr/sbin/nologin appuser && chown -R appuser:appuser /app
USER appuser

EXPOSE 3001
ENTRYPOINT ["dotnet", "Dashboard.dll"]
