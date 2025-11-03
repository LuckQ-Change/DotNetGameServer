# syntax=docker/dockerfile:1

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./src/ServerHost/ServerHost.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/publish .

# Add bash for TCP healthchecks
RUN apt-get update && apt-get install -y bash && rm -rf /var/lib/apt/lists/*

# Default environment
ENV ASPNETCORE_ENVIRONMENT=Production \
    SERVICE=directory \
    PORT=5500

# Container healthcheck: simple TCP connect to local PORT
HEALTHCHECK --interval=20s --timeout=3s --start-period=10s --retries=3 \
  CMD bash -c 'echo > /dev/tcp/127.0.0.1/$PORT' || exit 1

ENTRYPOINT ["dotnet", "ServerHost.dll"]