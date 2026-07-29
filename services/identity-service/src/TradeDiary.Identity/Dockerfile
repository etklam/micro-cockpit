FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/identity-service/src/TradeDiary.Identity/TradeDiary.Identity.csproj services/identity-service/src/TradeDiary.Identity/
COPY platform/TradeDiary.Authorization/TradeDiary.Authorization.csproj platform/TradeDiary.Authorization/
RUN dotnet restore services/identity-service/src/TradeDiary.Identity/TradeDiary.Identity.csproj
COPY services/identity-service/src/TradeDiary.Identity/ services/identity-service/src/TradeDiary.Identity/
COPY platform/TradeDiary.Authorization/ platform/TradeDiary.Authorization/
RUN dotnet publish services/identity-service/src/TradeDiary.Identity/TradeDiary.Identity.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /keys && chown "$APP_UID:$APP_UID" /keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.Identity.dll"]
