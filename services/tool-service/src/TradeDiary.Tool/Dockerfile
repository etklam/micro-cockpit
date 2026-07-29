FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/tool-service/src/TradeDiary.Tool/TradeDiary.Tool.csproj services/tool-service/src/TradeDiary.Tool/
COPY platform/TradeDiary.Authorization/TradeDiary.Authorization.csproj platform/TradeDiary.Authorization/
RUN dotnet restore services/tool-service/src/TradeDiary.Tool/TradeDiary.Tool.csproj
COPY services/tool-service/src/TradeDiary.Tool/ services/tool-service/src/TradeDiary.Tool/
COPY platform/TradeDiary.Authorization/ platform/TradeDiary.Authorization/
RUN dotnet publish services/tool-service/src/TradeDiary.Tool/TradeDiary.Tool.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","TradeDiary.Tool.dll"]
