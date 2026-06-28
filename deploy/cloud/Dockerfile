# Pos.Api as a Linux container for the HQ/cloud (multi-tenant SaaS) tier.
# The same binary runs on-prem as a Windows Service; UseWindowsService() is a no-op on Linux.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the csproj first for layer caching, then publish.
COPY . .
RUN dotnet restore src/Pos.Api/Pos.Api.csproj
RUN dotnet publish src/Pos.Api/Pos.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# pg_dump 17 (matching the Postgres server) so the Production startup migrator can take its mandatory
# pre-migration backup of a POPULATED shared DB before applying schema changes. (A fresh/empty DB
# migrates straight through and never invokes it.)
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates gnupg && \
    . /etc/os-release && \
    curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor -o /usr/share/keyrings/pgdg.gpg && \
    echo "deb [signed-by=/usr/share/keyrings/pgdg.gpg] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list && \
    apt-get update && apt-get install -y --no-install-recommends postgresql-client-17 && \
    apt-get purge -y curl gnupg && apt-get autoremove -y && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "Pos.Api.dll"]
