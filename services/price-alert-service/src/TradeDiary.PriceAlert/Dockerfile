FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/price-alert-service/src/TradeDiary.PriceAlert/TradeDiary.PriceAlert.csproj .
RUN dotnet restore
COPY services/price-alert-service/src/TradeDiary.PriceAlert/ .
RUN dotnet publish -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet","TradeDiary.PriceAlert.dll"]
