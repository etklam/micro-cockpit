FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/rotation-service/src/TradeDiary.Rotation/TradeDiary.Rotation.csproj .
RUN dotnet restore
COPY services/rotation-service/src/TradeDiary.Rotation/ .
RUN dotnet publish -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "TradeDiary.Rotation.dll"]
