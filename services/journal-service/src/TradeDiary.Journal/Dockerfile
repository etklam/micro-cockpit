FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/journal-service/src/TradeDiary.Journal/TradeDiary.Journal.csproj .
RUN dotnet restore TradeDiary.Journal.csproj
COPY services/journal-service/src/TradeDiary.Journal/ .
RUN dotnet publish TradeDiary.Journal.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.Journal.dll"]

