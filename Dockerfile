# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy solution and project files first (layer cache optimisation)
COPY FinanceControl.sln ./
COPY src/FinanceControl.API/FinanceControl.API.csproj ./src/FinanceControl.API/
COPY tests/FinanceControl.UnitTests/FinanceControl.UnitTests.csproj ./tests/FinanceControl.UnitTests/
COPY tests/FinanceControl.IntegrationTests/FinanceControl.IntegrationTests.csproj ./tests/FinanceControl.IntegrationTests/

RUN dotnet restore

# Copy the full source
COPY . .

# Publish the API (tests run in CI/locally via `dotnet test` with
# `docker compose up -d postgres` — never inside the image build).
RUN dotnet publish src/FinanceControl.API/FinanceControl.API.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl powers the docker-compose healthcheck (not shipped in the Ubuntu base image).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Non-root user for security (Ubuntu base image: groupadd/useradd — addgroup is Debian-only).
RUN groupadd --system appgroup && useradd --system --gid appgroup appuser
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "FinanceControl.API.dll"]
