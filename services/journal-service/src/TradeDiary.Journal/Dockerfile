FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Preserve repo-relative ProjectReference paths for contracts/TradeDiary.Events.
COPY services/journal-service/src/TradeDiary.Journal/TradeDiary.Journal.csproj services/journal-service/src/TradeDiary.Journal/
COPY contracts/TradeDiary.Events/TradeDiary.Events.csproj contracts/TradeDiary.Events/
RUN dotnet restore services/journal-service/src/TradeDiary.Journal/TradeDiary.Journal.csproj
COPY services/journal-service/src/TradeDiary.Journal/ services/journal-service/src/TradeDiary.Journal/
COPY contracts/TradeDiary.Events/ contracts/TradeDiary.Events/
RUN dotnet publish services/journal-service/src/TradeDiary.Journal/TradeDiary.Journal.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.Journal.dll"]
