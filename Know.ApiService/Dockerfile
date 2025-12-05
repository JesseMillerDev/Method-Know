# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Know.ApiService/Know.ApiService.csproj", "Know.ApiService/"]
COPY ["Know.Shared/Know.Shared.csproj", "Know.Shared/"]
RUN dotnet restore "Know.ApiService/Know.ApiService.csproj"
COPY . .
WORKDIR "/src/Know.ApiService"
RUN dotnet build "Know.ApiService.csproj" -c $BUILD_CONFIGURATION -o /app/build
RUN dotnet publish "Know.ApiService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Model Downloader Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS model_downloader
WORKDIR /models
RUN curl -L -o Llama-3.2-3B-Instruct-Q4_K_M.gguf https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf

# Serve stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
# Copy model from downloader stage
COPY --from=model_downloader /models/Llama-3.2-3B-Instruct-Q4_K_M.gguf /app/Models/
ENTRYPOINT ["dotnet", "Know.ApiService.dll"]
