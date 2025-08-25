# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY PlaywrightHub.csproj ./
RUN dotnet restore PlaywrightHub.csproj
COPY . .
RUN dotnet publish PlaywrightHub.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# ✅ Install just curl for the healthcheck (small)
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl; \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:5000 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 5000
HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=20s CMD curl -fsSL http://127.0.0.1:5000/health || exit 1
ENTRYPOINT ["dotnet","PlaywrightHub.dll"]