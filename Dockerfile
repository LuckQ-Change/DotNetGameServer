# syntax=docker/dockerfile:1

# Which server to build/run. Override per service via build args.
ARG SERVER_PROJECT=src/Servers/DirectoryServer/DirectoryServer.csproj
ARG SERVER_DLL=DirectoryServer.dll

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG SERVER_PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "$SERVER_PROJECT" -c Release -o /app/publish

# Runtime stage (ASP.NET shared framework is required: the host uses WebApplication)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
ARG SERVER_DLL
WORKDIR /app
COPY --from=build /app/publish .

# Add bash for TCP healthchecks
RUN apt-get update && apt-get install -y bash && rm -rf /var/lib/apt/lists/*

# Default environment. PORT is used only by the healthcheck; all server config
# (port, healthCheckUrl, secrets, heartbeat target) is passed as CLI args via `command:`,
# because this project resolves config from appsettings + CLI only (CLI wins).
ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=5500 \
    SERVER_DLL=${SERVER_DLL}

# Container healthcheck: simple TCP connect to local PORT
HEALTHCHECK --interval=20s --timeout=3s --start-period=10s --retries=3 \
  CMD bash -c 'echo > /dev/tcp/127.0.0.1/$PORT' || exit 1

# CMD args are appended to `dotnet <dll>`; override per service in docker-compose.
ENTRYPOINT ["sh", "-c", "exec dotnet \"$SERVER_DLL\" \"$@\"", "sh"]
CMD ["--port", "5500", "--healthCheckUrl", "http://+:8080"]
