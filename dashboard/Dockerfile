# syntax=docker/dockerfile:1.4
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy source
COPY . .
# Restore and publish using robust path detection to support different build contexts
WORKDIR /src
RUN --mount=type=cache,id=nuget-dashboard,target=/root/.nuget/packages,sharing=locked if [ -f "Dashboard.csproj" ]; then dotnet restore Dashboard.csproj; else dotnet restore dashboard/Dashboard.csproj; fi
RUN --mount=type=cache,id=nuget-dashboard,target=/root/.nuget/packages,sharing=locked if [ -f "Dashboard.csproj" ]; then dotnet publish Dashboard.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false; else dotnet publish dashboard/Dashboard.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false; fi

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
