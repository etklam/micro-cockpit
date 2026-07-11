FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/stock-research-service/src/TradeDiary.StockResearch/TradeDiary.StockResearch.csproj .
RUN dotnet restore TradeDiary.StockResearch.csproj
COPY services/stock-research-service/src/TradeDiary.StockResearch/ .
RUN dotnet publish TradeDiary.StockResearch.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.StockResearch.dll"]
