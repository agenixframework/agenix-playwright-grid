FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["housekeeping-service/HousekeepingService.csproj", "housekeeping-service/"]
RUN dotnet restore "housekeeping-service/HousekeepingService.csproj"
COPY housekeeping-service/ housekeeping-service/
WORKDIR "/src/housekeeping-service"
RUN dotnet build "HousekeepingService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HousekeepingService.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HousekeepingService.dll"]
