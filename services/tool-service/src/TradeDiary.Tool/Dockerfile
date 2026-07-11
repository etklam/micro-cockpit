FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/tool-service/src/TradeDiary.Tool/TradeDiary.Tool.csproj .
RUN dotnet restore
COPY services/tool-service/src/TradeDiary.Tool/ .
RUN dotnet publish -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","TradeDiary.Tool.dll"]
