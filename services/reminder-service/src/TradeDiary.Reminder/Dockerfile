FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Preserve repo-relative ProjectReference paths for contracts/TradeDiary.Events.
COPY services/reminder-service/src/TradeDiary.Reminder/TradeDiary.Reminder.csproj services/reminder-service/src/TradeDiary.Reminder/
COPY contracts/TradeDiary.Events/TradeDiary.Events.csproj contracts/TradeDiary.Events/
RUN dotnet restore services/reminder-service/src/TradeDiary.Reminder/TradeDiary.Reminder.csproj
COPY services/reminder-service/src/TradeDiary.Reminder/ services/reminder-service/src/TradeDiary.Reminder/
COPY contracts/TradeDiary.Events/ contracts/TradeDiary.Events/
RUN dotnet publish services/reminder-service/src/TradeDiary.Reminder/TradeDiary.Reminder.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeDiary.Reminder.dll"]
