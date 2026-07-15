FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY platform/postgres/migrator/TradeDiary.DatabaseMigrator/TradeDiary.DatabaseMigrator.csproj migrator/
RUN dotnet restore migrator/TradeDiary.DatabaseMigrator.csproj
COPY platform/postgres/migrator/TradeDiary.DatabaseMigrator/ migrator/
RUN dotnet publish migrator/TradeDiary.DatabaseMigrator.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

FROM postgres:17
COPY --from=runtime /usr/share/dotnet/ /usr/share/dotnet/
WORKDIR /tool
COPY --from=build /out/ ./
COPY platform/postgres/migrations/ /migrations/
COPY platform/postgres/baseline/ /baseline/
COPY platform/postgres/roles/ /roles/
RUN chmod 0555 /roles/apply.sh
ENV MIGRATIONS_PATH=/migrations
ENV BASELINE_FINGERPRINT_PATH=/baseline/legacy-v1-schema.json
ENTRYPOINT ["/usr/share/dotnet/dotnet", "/tool/TradeDiary.DatabaseMigrator.dll"]
