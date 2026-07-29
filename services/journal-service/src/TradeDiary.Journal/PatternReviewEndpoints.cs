using Npgsql;

static class PatternReviewEndpoints
{
    internal static void Map(RouteGroupBuilder journal)
    {
        journal.MapGet("/pattern-review", async (
            string range,
            DateOnly? from,
            DateOnly? to,
            HttpRequest request,
            NpgsqlDataSource db,
            TimeProvider timeProvider) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var dates = ResolveRange(range, from, to, timeProvider.GetUtcNow().UtcDateTime);
            if (dates is null) return Results.Problem("invalid_range", statusCode: 400);
            var (start, end) = dates.Value;
            var startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            await using var count = db.CreateCommand("""
                SELECT count(*) FROM journal.expectation_reviews
                WHERE user_id=$1 AND deleted_at IS NULL AND created_at >= $2 AND created_at < $3
                """);
            count.Parameters.AddWithValue(userId);
            count.Parameters.AddWithValue(startUtc);
            count.Parameters.AddWithValue(endUtc);
            var denominator = Convert.ToInt32(await count.ExecuteScalarAsync());

            var labels = ExpectationReviewEndpoints.SystemIssues
                .Select(item => new MutablePatternLabel(ReasoningLabelKind.issue, item.Key, item.Value, true))
                .Concat(ExpectationReviewEndpoints.SystemStrengths
                    .Select(item => new MutablePatternLabel(ReasoningLabelKind.strength, item.Key, item.Value, true)))
                .ToDictionary(item => $"{item.Kind}:{item.Key}", StringComparer.Ordinal);

            await using var command = db.CreateCommand("""
                SELECT l.kind,coalesce(l.system_key,l.custom_label_id::text) AS label_key,
                       coalesce(c.name,l.system_key) AS label_name,r.expectation_id
                FROM journal.expectation_reviews r
                JOIN journal.expectation_review_labels l ON l.review_id=r.id
                LEFT JOIN journal.reasoning_labels c ON c.id=l.custom_label_id AND c.user_id=r.user_id
                WHERE r.user_id=$1 AND r.deleted_at IS NULL AND r.created_at >= $2 AND r.created_at < $3
                ORDER BY r.expectation_id,l.id
                """);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(startUtc);
            command.Parameters.AddWithValue(endUtc);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var kind = Enum.Parse<ReasoningLabelKind>(reader.GetString(0));
                var key = reader.GetString(1);
                var dictionaryKey = $"{kind}:{key}";
                if (!labels.TryGetValue(dictionaryKey, out var label))
                {
                    label = new MutablePatternLabel(kind, key, reader.GetString(2), false);
                    labels.Add(dictionaryKey, label);
                }
                var expectationId = reader.GetGuid(3);
                label.Evidence.Add(new PatternEvidenceResponse(
                    expectationId,
                    $"/today?expectationId={expectationId}"));
            }

            var response = labels.Values
                .Select(label => new PatternLabelResponse(
                    label.Kind, label.Key, label.Name, label.System,
                    label.Evidence.Count, denominator, label.Evidence))
                .OrderByDescending(label => label.Count)
                .ThenBy(label => label.Kind)
                .ThenBy(label => label.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Results.Ok(new PatternReviewResponse(start, end, denominator, response));
        })
        .Produces<PatternReviewResponse>(200)
        .ProducesProblem(400)
        .ProducesProblem(401);

        journal.MapGet("/discipline-principles", async (HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            return Results.Ok(new CollectionResponse<DisciplinePrincipleResponse>(await ListPrinciples(db, userId)));
        }).Produces<CollectionResponse<DisciplinePrincipleResponse>>(200).ProducesProblem(401);

        journal.MapGet("/discipline-principles/today", async (HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var principle = (await ListPrinciples(db, userId)).SingleOrDefault(item => item.SelectedForToday);
            return principle is null ? Results.Problem("not_found", statusCode: 404) : Results.Ok(principle);
        }).Produces<DisciplinePrincipleResponse>(200).ProducesProblem(401).ProducesProblem(404);

        journal.MapPost("/discipline-principles", async (DisciplinePrincipleCreate input, HttpRequest request, NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var content = input.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 280)
                return Results.Problem("invalid_content", statusCode: 400);
            var id = Guid.NewGuid();
            await using var command = db.CreateCommand("""
                INSERT INTO journal.discipline_principles(id,user_id,content)
                VALUES($1,$2,$3)
                RETURNING created_at,updated_at
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(content);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return Results.Created($"/internal/discipline-principles/{id}", new DisciplinePrincipleResponse(
                id, content, DisciplinePrincipleStatus.active, false,
                Utc(reader.GetDateTime(0)), Utc(reader.GetDateTime(1))));
        }).Produces<DisciplinePrincipleResponse>(201).ProducesProblem(400).ProducesProblem(401);

        journal.MapPut("/discipline-principles/{id:guid}", async (
            Guid id,
            DisciplinePrincipleUpdate input,
            HttpRequest request,
            NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            var content = input.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 280)
                return Results.Problem("invalid_content", statusCode: 400);
            await using var command = db.CreateCommand("""
                UPDATE journal.discipline_principles
                SET content=$3,status=$4,
                    selected_for_today=selected_for_today AND $4='active',
                    updated_at=now()
                WHERE id=$1 AND user_id=$2
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(content);
            command.Parameters.AddWithValue(input.Status.ToString());
            return await command.ExecuteNonQueryAsync() == 0
                ? Results.Problem("not_found", statusCode: 404)
                : Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

        journal.MapPost("/discipline-principles/{id:guid}/select", async (
            Guid id,
            HttpRequest request,
            NpgsqlDataSource db) =>
        {
            if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var exists = new NpgsqlCommand("""
                SELECT EXISTS(
                    SELECT 1 FROM journal.discipline_principles
                    WHERE id=$1 AND user_id=$2 AND status='active'
                )
                """, connection, tx);
            exists.Parameters.AddWithValue(id);
            exists.Parameters.AddWithValue(userId);
            if (!(bool)(await exists.ExecuteScalarAsync())!)
                return Results.Problem("not_found", statusCode: 404);
            await using var clear = new NpgsqlCommand("""
                UPDATE journal.discipline_principles
                SET selected_for_today=false,updated_at=now()
                WHERE user_id=$1 AND selected_for_today
                """, connection, tx);
            clear.Parameters.AddWithValue(userId);
            await clear.ExecuteNonQueryAsync();
            await using var select = new NpgsqlCommand("""
                UPDATE journal.discipline_principles
                SET selected_for_today=true,updated_at=now()
                WHERE id=$1 AND user_id=$2
                """, connection, tx);
            select.Parameters.AddWithValue(id);
            select.Parameters.AddWithValue(userId);
            await select.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(404);
    }

    private static (DateOnly Start, DateOnly End)? ResolveRange(
        string range, DateOnly? from, DateOnly? to, DateTime utcNow)
    {
        var today = DateOnly.FromDateTime(utcNow);
        return range switch
        {
            "weekly" when from is null && to is null => (today.AddDays(-6), today),
            "monthly" when from is null && to is null => (new DateOnly(today.Year, today.Month, 1), today),
            "custom" when from is not null && to is not null && from <= to => (from.Value, to.Value),
            _ => null,
        };
    }

    private static async Task<List<DisciplinePrincipleResponse>> ListPrinciples(NpgsqlDataSource db, Guid userId)
    {
        await using var command = db.CreateCommand("""
            SELECT id,content,status,selected_for_today,created_at,updated_at
            FROM journal.discipline_principles
            WHERE user_id=$1
            ORDER BY selected_for_today DESC,
                     CASE status WHEN 'active' THEN 0 WHEN 'disabled' THEN 1 ELSE 2 END,
                     created_at DESC,id
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<DisciplinePrincipleResponse>();
        while (await reader.ReadAsync())
            result.Add(new(
                reader.GetGuid(0), reader.GetString(1),
                Enum.Parse<DisciplinePrincipleStatus>(reader.GetString(2)),
                reader.GetBoolean(3), Utc(reader.GetDateTime(4)), Utc(reader.GetDateTime(5))));
        return result;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed class MutablePatternLabel(ReasoningLabelKind kind, string key, string name, bool system)
    {
        internal ReasoningLabelKind Kind { get; } = kind;
        internal string Key { get; } = key;
        internal string Name { get; } = name;
        internal bool System { get; } = system;
        internal List<PatternEvidenceResponse> Evidence { get; } = [];
    }
}
