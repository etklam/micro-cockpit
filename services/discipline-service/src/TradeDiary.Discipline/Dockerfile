FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/discipline-service/src/TradeDiary.Discipline/TradeDiary.Discipline.csproj .
RUN dotnet restore TradeDiary.Discipline.csproj
COPY services/discipline-service/src/TradeDiary.Discipline/ .
RUN dotnet publish TradeDiary.Discipline.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.Discipline.dll"]

