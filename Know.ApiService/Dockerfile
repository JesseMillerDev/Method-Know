# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Know.ApiService.csproj", "."]
RUN dotnet restore "./Know.ApiService.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./Know.ApiService.csproj" -c $BUILD_CONFIGURATION -o /app/build
RUN dotnet publish "./Know.ApiService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Serve stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Know.ApiService.dll"]
