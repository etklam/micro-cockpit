using System.Text.Json;

public record DiaryWrite(DateOnly LocalDate, string Title, string? Content, IReadOnlyList<string>? Tags = null);
record QuickNote(DateOnly LocalDate, string Content, Guid? TargetDiaryId);
public record DiaryResponse(Guid Id, DateOnly LocalDate, string Title, string Content, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<string> Tags);
record TransactionWrite(string Symbol, string Side, decimal Quantity, decimal Price, string Currency, DateTime TradedAt, string? Notes);
record TransactionResponse(Guid Id, Guid DiaryId, string Symbol, string Side, decimal Quantity, decimal Price, string Currency, DateTime TradedAt, string Notes, DateTime CreatedAt, DateTime UpdatedAt);
record StoredResult(int StatusCode, string? Location, JsonElement Body);
record QuickNoteResponse(Guid? DiaryId, bool Appended);
record DiaryDaySummaryItem(DateOnly LocalDate, long DiaryCount, long TransactionCount);
record CollectionResponse<T>(List<T> Items);

// ponytail: WithOpenApi parameter mutations are dropped by .NET 10 doc generation (hence its deprecation),
// so the Idempotency-Key header is surfaced via a marker + operation transformer instead.
sealed record IdempotencyKeyHeaderMarker;
