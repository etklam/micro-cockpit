FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/partner-service/src/TradeDiary.Partner/TradeDiary.Partner.csproj .
RUN dotnet restore
COPY services/partner-service/src/TradeDiary.Partner/ .
RUN dotnet publish -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","TradeDiary.Partner.dll"]
