FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY . .
RUN dotnet publish src/OpenContext.AgentBridge.Server/OpenContext.AgentBridge.Server.csproj \
    -c Release \
    -o /app/publish \
    --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
RUN mkdir -p /workspace

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    AGENTBRIDGE_SERVER_WORKSPACE=/workspace \
    AGENTBRIDGE_MODEL_PROVIDER=gateway \
    AGENTBRIDGE_LOG_MODEL_TRAFFIC=false

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OpenContext.AgentBridge.Server.dll"]
