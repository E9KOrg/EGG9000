# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["EGG9000.APILinkSite/EGG9000.APILinkSite.csproj", "EGG9000.APILinkSite/"]
COPY ["EGG9000.Common/EGG9000.Common.csproj", "EGG9000.Common/"]
RUN dotnet restore "./EGG9000.APILinkSite/EGG9000.APILinkSite.csproj"
COPY . .
WORKDIR "/src/EGG9000.APILinkSite"
RUN dotnet build "./EGG9000.APILinkSite.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./EGG9000.APILinkSite.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EGG9000.APILinkSite.dll"]