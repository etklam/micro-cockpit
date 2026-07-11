FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/reminder-service/src/TradeDiary.Reminder/TradeDiary.Reminder.csproj .
RUN dotnet restore TradeDiary.Reminder.csproj
COPY services/reminder-service/src/TradeDiary.Reminder/ .
RUN dotnet publish TradeDiary.Reminder.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.Reminder.dll"]

