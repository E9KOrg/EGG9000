# Multi-stage build for .NET 10 worker
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

USER app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["EGG9000.Bot/EGG9000.Bot.csproj", "EGG9000.Bot/"]
COPY ["EGG9000.Common/EGG9000.Common.csproj", "EGG9000.Common/"]
RUN dotnet restore "./EGG9000.Bot/EGG9000.Bot.csproj"
COPY . .
RUN printf 'Docker build\nunknown\nDocker\n0' > /src/EGG9000.Bot/version.txt
ENV DOCKER_BUILD=true
WORKDIR "/src/EGG9000.Bot"
RUN dotnet publish "./EGG9000.Bot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EGG9000.Bot.dll"]