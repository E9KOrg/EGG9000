# Multi-stage build for .NET 9 worker that depends on ASP.NET shared framework
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Create a non-root user for improved security (optional)
RUN useradd --create-home appuser && chown -R appuser:appuser /app
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["EGG9000.Bot/EGG9000.Bot.csproj", "EGG9000.Bot/"]
COPY ["EGG9000.Common/EGG9000.Common.csproj", "EGG9000.Common/"]
RUN dotnet restore "./EGG9000.Bot/EGG9000.Bot.csproj"
COPY . .
WORKDIR "/src/EGG9000.Bot"
RUN dotnet build "./EGG9000.Bot.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./EGG9000.Bot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EGG9000.Bot.dll"]