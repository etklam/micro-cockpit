using System.Text.Json;
using System.Text.Json.Serialization;

public enum CapabilityStatus
{
    Available,
    Empty,
    Unavailable
}

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName);

public sealed record AppBootstrapResponse(
    CurrentUserResponse CurrentUser,
    string Timezone,
    string JournalDayRollover,
    string BaseCurrency,
    string Appearance,
    string Locale,
    string AccentTheme,
    string Role,
    string AccountType,
    DateOnly CurrentJournalDay,
    IReadOnlyList<string> AvailableProductAreas);

public sealed record UserSettingsResponse(
    string Email,
    string DisplayName,
    string Timezone,
    string JournalDayRollover,
    string BaseCurrency,
    string Appearance,
    string Locale,
    string AccentTheme,
    DateTime UpdatedAt);

public sealed record UserSettingsWrite(
    string DisplayName,
    string Timezone,
    string JournalDayRollover,
    string BaseCurrency,
    string Appearance,
    string Locale,
    string AccentTheme);

public sealed record AccountExportResponse(
    int SchemaVersion,
    DateTime ExportedAt,
    JsonElement? Identity,
    JsonElement? Journal,
    JsonElement? Tools);

public sealed record AccountDeletionWrite(string Confirmation);

public sealed record CalendarResponse(
    int Year,
    int Month,
    IReadOnlyList<CalendarDayResponse> Days);

public sealed record CalendarDayResponse(
    DateOnly Date,
    Guid? MarketObservationId,
    long UpdateCount,
    long? ReadyForReviewCount);

internal sealed record IdentityUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Timezone,
    string JournalDayRollover,
    string BaseCurrency,
    string Role,
    string AccountType,
    string Status,
    int StatusVersion,
    string Appearance = "system",
    string Locale = "en",
    string AccentTheme = "green");

internal sealed record IdentitySettingsResponse(
    string Email,
    string DisplayName,
    string Timezone,
    string JournalDayRollover,
    string BaseCurrency,
    string Appearance,
    string Locale,
    string AccentTheme,
    DateTime UpdatedAt);

internal sealed record IdentityTokensResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken);
internal sealed record RefreshRequest(string RefreshToken);

internal sealed record CollectionResponse<T>(IReadOnlyList<T> Items);
internal sealed record MarketObservationDayFact(DateOnly Date, Guid MarketObservationId, long UpdateCount, long? ReadyForReviewCount);

public sealed record EdgeProblemDetails(
    string Code,
    string Title,
    int Status,
    string Detail,
    string CorrelationId);

public sealed record SessionTokensResponse(string AccessToken, DateTime ExpiresAt);
