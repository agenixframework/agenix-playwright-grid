# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["ingestion/IngestionService.csproj", "ingestion/"]
COPY ["Agenix.PlaywrightGrid.Domain/Agenix.PlaywrightGrid.Domain.csproj", "Agenix.PlaywrightGrid.Domain/"]
RUN dotnet restore "ingestion/IngestionService.csproj"

# Copy source and build
COPY ingestion/ ingestion/
COPY Agenix.PlaywrightGrid.Domain/ Agenix.PlaywrightGrid.Domain/
WORKDIR "/src/ingestion"
RUN dotnet build "IngestionService.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "IngestionService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd -m -u 1000 appuser && chown -R appuser:appuser /app
USER appuser

COPY --from=publish /app/publish .

EXPOSE 8080 9091

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "IngestionService.dll"]
