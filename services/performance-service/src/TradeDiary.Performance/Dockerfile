FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/performance-service/src/TradeDiary.Performance/TradeDiary.Performance.csproj .
RUN dotnet restore TradeDiary.Performance.csproj
COPY services/performance-service/src/TradeDiary.Performance/ .
RUN dotnet publish TradeDiary.Performance.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.Performance.dll"]

