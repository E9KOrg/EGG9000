# Multi-stage build for ASP.NET Core 10 web application
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Install curl for Docker health checks
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

USER app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["EGG9000.Site/EGG9000.Site.csproj", "EGG9000.Site/"]
COPY ["EGG9000.Common/EGG9000.Common.csproj", "EGG9000.Common/"]
RUN dotnet restore "./EGG9000.Site/EGG9000.Site.csproj"
COPY . .
WORKDIR "/src/EGG9000.Site"
RUN dotnet build "./EGG9000.Site.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./EGG9000.Site.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EGG9000.Site.dll"]