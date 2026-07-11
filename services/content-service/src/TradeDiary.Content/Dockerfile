FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/content-service/src/TradeDiary.Content/TradeDiary.Content.csproj .
RUN dotnet restore
COPY services/content-service/src/TradeDiary.Content/ .
RUN dotnet publish -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","TradeDiary.Content.dll"]
