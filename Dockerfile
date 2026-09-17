# syntax=docker/dockerfile:1.6
# Build context: solution root (scoreClientSocket/)
# scoreClientSocket.csproj references ../Modal so the context must include both folders.

ARG DOTNET_VERSION=10.0

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy project files first — restore is cached until a .csproj changes.
COPY ["scoreClientSocket/scoreClientSocket.csproj", "scoreClientSocket/"]
COPY ["BusinessServices/BusinessServices.csproj", "BusinessServices/"]
COPY ["Modal/Modal.csproj", "Modal/"]

RUN dotnet restore "scoreClientSocket/scoreClientSocket.csproj" \
        --runtime linux-x64 \
        /p:PublishReadyToRun=true

COPY . .

RUN dotnet publish "scoreClientSocket/scoreClientSocket.csproj" \
        -c Release \
        -o /app/publish \
        --runtime linux-x64 \
        --self-contained false \
        --no-restore \
        /p:UseAppHost=false \
        /p:PublishReadyToRun=true \
        /p:PublishReadyToRunComposite=true \
        /p:TieredCompilation=true \
        /p:TieredPGO=true

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
# curl is needed for the Docker healthcheck (not present in the slim aspnet image).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_ReadyToRun=1 \
    DOTNET_TieredCompilation=1 \
    DOTNET_TieredPGO=1 \
    DOTNET_TC_QuickJitForLoops=1 \
    DOTNET_gcServer=0 \
    DOTNET_GCDynamicAdaptationMode=1 \
    TZ=Asia/Kolkata

# Cloud Run and Railway both inject a PORT env var at runtime (Railway's is dynamic,
# not fixed at 8080) — the entrypoint below binds to whatever PORT is provided and
# falls back to 8080 for local `docker run`/compose.
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet scoreClientSocket.dll"]
