FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY gateway/TradeDiary.EdgeApi/TradeDiary.EdgeApi.csproj gateway/TradeDiary.EdgeApi/
COPY platform/TradeDiary.Authorization/TradeDiary.Authorization.csproj platform/TradeDiary.Authorization/
RUN dotnet restore gateway/TradeDiary.EdgeApi/TradeDiary.EdgeApi.csproj
COPY gateway/TradeDiary.EdgeApi/ gateway/TradeDiary.EdgeApi/
COPY platform/TradeDiary.Authorization/ platform/TradeDiary.Authorization/
RUN dotnet publish gateway/TradeDiary.EdgeApi/TradeDiary.EdgeApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.EdgeApi.dll"]
