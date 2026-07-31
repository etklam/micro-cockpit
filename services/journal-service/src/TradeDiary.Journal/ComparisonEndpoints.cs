using Npgsql;
using NpgsqlTypes;

static class ComparisonEndpoints
{
    internal static void Map(RouteGroupBuilder journal) =>
        journal.MapGet("/comparison", ReadAsync)
            .Produces<OwnerComparisonResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401);

    private static async Task<IResult> ReadAsync(
        Guid agentUserId,
        DateOnly from,
        DateOnly to,
        HttpRequest request,
        NpgsqlDataSource db,
        IHttpClientFactory clients,
        TimeProvider timeProvider,
        string? subjectType = null,
        string? subject = null,
        Guid? instrumentId = null)
    {
        if (!JournalAccess.TryUser(request, out var humanId)) return Results.Unauthorized();
        if (agentUserId == Guid.Empty || from > to || to.DayNumber - from.DayNumber > 366)
            return Results.Problem("invalid_comparison", statusCode: 400);

        ObservationSubjectType? type = null;
        var name = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        if (subjectType is not null)
        {
            if (!Enum.TryParse<ObservationSubjectType>(subjectType, out var parsedType)
                || parsedType == ObservationSubjectType.instrument)
                return Results.Problem("invalid_subject", statusCode: 400);
            type = parsedType;
        }
        if ((type is null) != (name is null)
            || name?.Length > 120
            || (instrumentId is null) == (type is null))
            return Results.Problem("invalid_subject", statusCode: 400);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var grantAvailable = await HasGrantAsync(
            db, humanId, agentUserId, from, to, type, name, instrumentId, now);
        var human = await ReadOwnerAsync(
            db, clients, humanId, humanId, ComparisonOwnerType.human, from, to,
            type, name, instrumentId, now, constrainToGrant: false, request.HttpContext.RequestAborted);
        var agent = grantAvailable
            ? await ReadOwnerAsync(
                db, clients, humanId, agentUserId, ComparisonOwnerType.agent, from, to,
                type, name, instrumentId, now, constrainToGrant: true, request.HttpContext.RequestAborted)
            : new ComparisonOwnerResponse(agentUserId, ComparisonOwnerType.agent, ComparisonAvailability.unavailable, []);

        return Results.Ok(new OwnerComparisonResponse(human, agent, Compare(human, agent)));
    }

    private static async Task<bool> HasGrantAsync(
        NpgsqlDataSource db,
        Guid humanId,
        Guid agentId,
        DateOnly from,
        DateOnly to,
        ObservationSubjectType? type,
        string? subject,
        Guid? instrumentId,
        DateTime now)
    {
        await using var command = db.CreateCommand("""
            SELECT EXISTS (
                SELECT 1 FROM journal.agent_access_grants g
                WHERE g.owner_user_id=$1 AND g.agent_user_id=$2
                  AND g.revoked_at IS NULL AND (g.expires_at IS NULL OR g.expires_at>$3)
                  AND g.from_date<=$5 AND g.to_date>=$4
                  AND (
                    (g.subject_type IS NULL AND g.instrument_id IS NULL)
                    OR ($6::text IS NOT NULL AND g.subject_type=$6 AND lower(g.subject_name)=lower($7))
                    OR ($8::uuid IS NOT NULL AND g.instrument_id=$8)
                  )
            )
            """);
        AddParameters(command, humanId, agentId, now, from, to, type, subject, instrumentId);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<ComparisonOwnerResponse> ReadOwnerAsync(
        NpgsqlDataSource db,
        IHttpClientFactory clients,
        Guid humanId,
        Guid ownerId,
        ComparisonOwnerType ownerType,
        DateOnly from,
        DateOnly to,
        ObservationSubjectType? type,
        string? subject,
        Guid? instrumentId,
        DateTime now,
        bool constrainToGrant,
        CancellationToken cancellationToken)
    {
        var grantPredicate = constrainToGrant
            ? """
               AND EXISTS (
                 SELECT 1 FROM journal.agent_access_grants g
                 WHERE g.owner_user_id=$1 AND g.agent_user_id=$2
                   AND g.revoked_at IS NULL AND (g.expires_at IS NULL OR g.expires_at>$3)
                   AND o.journal_day BETWEEN g.from_date AND g.to_date
                   AND (
                     (g.subject_type IS NULL AND g.instrument_id IS NULL)
                     OR journal.subject_matches(u.primary_subject,u.related_subjects,g.subject_type,g.subject_name,g.instrument_id)
                   )
               )
              """
            : "";
        await using var command = db.CreateCommand($"""
            SELECT o.journal_day,
                   u.id,u.content,u.recorded_at,u.updated_at,u.signal,u.interpretation,u.mental_state,u.tags,
                   u.primary_subject::text,u.related_subjects::text,u.evidence::text,
                   e.id,e.expected_behavior,e.deadline,e.invalidation_condition,e.confidence,e.market,
                   r.outcome,r.reasoning_quality,r.explanation
            FROM journal.market_observations o
            JOIN journal.observation_updates u
              ON u.market_observation_id=o.id AND u.user_id=o.user_id AND u.deleted_at IS NULL
            LEFT JOIN journal.expectations e
              ON e.observation_update_id=u.id AND e.user_id=u.user_id AND e.deleted_at IS NULL
            LEFT JOIN journal.expectation_reviews r
              ON r.expectation_id=e.id AND r.user_id=e.user_id AND r.deleted_at IS NULL
            WHERE o.user_id=$2 AND o.deleted_at IS NULL
              AND o.journal_day BETWEEN $4 AND $5
              AND journal.subject_matches(u.primary_subject,u.related_subjects,$6,$7,$8)
              {grantPredicate}
            ORDER BY o.journal_day DESC,u.recorded_at DESC,u.id,e.created_at DESC,e.id
            LIMIT 1000
            """);
        AddParameters(command, humanId, ownerId, now, from, to, type, subject, instrumentId);

        var ordered = new List<ComparisonObservationBuilder>();
        var byUpdate = new Dictionary<Guid, ComparisonObservationBuilder>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var updateId = reader.GetGuid(1);
                if (!byUpdate.TryGetValue(updateId, out var item))
                {
                    item = new(
                        reader.GetFieldValue<DateOnly>(0),
                        ObservationEnrichment.Read(reader, 1),
                        []);
                    byUpdate.Add(updateId, item);
                    ordered.Add(item);
                }
                if (!reader.IsDBNull(12))
                    item.Expectations.Add(new(
                        reader.GetGuid(12),
                        reader.GetString(13),
                        DateTime.SpecifyKind(reader.GetDateTime(14), DateTimeKind.Utc),
                        reader.GetString(15),
                        Enum.Parse<ExpectationConfidence>(reader.GetString(16)),
                        reader.GetString(17),
                        reader.IsDBNull(18) ? null : Enum.Parse<ExpectationOutcome>(reader.GetString(18)),
                        reader.IsDBNull(19) ? null : Enum.Parse<ReasoningQuality>(reader.GetString(19)),
                        reader.IsDBNull(20) ? null : reader.GetString(20)));
            }
        }

        var observations = (await Task.WhenAll(ordered.Select(async item => new ComparisonObservationResponse(
            item.JournalDay,
            await ObservationInstruments.AttachDailyCloseAsync(
                clients, item.Update, item.JournalDay, cancellationToken),
            item.Expectations)))).ToList();
        return new(
            ownerId,
            ownerType,
            observations.Count == 0 ? ComparisonAvailability.empty : ComparisonAvailability.available,
            observations);
    }

    private static void AddParameters(
        NpgsqlCommand command,
        Guid humanId,
        Guid ownerId,
        DateTime now,
        DateOnly from,
        DateOnly to,
        ObservationSubjectType? type,
        string? subject,
        Guid? instrumentId)
    {
        command.Parameters.AddWithValue(humanId);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = now });
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)type?.ToString() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)subject ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)instrumentId ?? DBNull.Value });
    }

    private static ComparisonDifferenceResponse Compare(
        ComparisonOwnerResponse human,
        ComparisonOwnerResponse agent)
    {
        var humanExpectation = human.Observations.SelectMany(item => item.Expectations).FirstOrDefault();
        var agentExpectation = agent.Observations.SelectMany(item => item.Expectations).FirstOrDefault();
        return new(
            humanExpectation?.Outcome is not null && agentExpectation?.Outcome is not null
                ? humanExpectation.Outcome == agentExpectation.Outcome
                : null,
            humanExpectation is not null && agentExpectation is not null
                ? ConfidenceValue(agentExpectation.Confidence) - ConfidenceValue(humanExpectation.Confidence)
                : null);
    }

    private static int ConfidenceValue(ExpectationConfidence value) => value switch
    {
        ExpectationConfidence.low => 1,
        ExpectationConfidence.medium => 2,
        _ => 3,
    };

    private sealed record ComparisonObservationBuilder(
        DateOnly JournalDay,
        ObservationUpdateResponse Update,
        List<ComparisonExpectationResponse> Expectations);
}

enum ComparisonOwnerType { human, agent }
enum ComparisonAvailability { available, empty, unavailable }
record ComparisonExpectationResponse(
    Guid Id,
    string ExpectedBehavior,
    DateTime Deadline,
    string InvalidationCondition,
    ExpectationConfidence Confidence,
    string Market,
    ExpectationOutcome? Outcome,
    ReasoningQuality? ReasoningQuality,
    string? ReviewExplanation);
record ComparisonObservationResponse(
    DateOnly JournalDay,
    ObservationUpdateResponse Update,
    IReadOnlyList<ComparisonExpectationResponse> Expectations);
record ComparisonOwnerResponse(
    Guid OwnerId,
    ComparisonOwnerType OwnerType,
    ComparisonAvailability Availability,
    IReadOnlyList<ComparisonObservationResponse> Observations);
record ComparisonDifferenceResponse(bool? OutcomeConsistent, int? ConfidenceDifference);
record OwnerComparisonResponse(
    ComparisonOwnerResponse Human,
    ComparisonOwnerResponse Agent,
    ComparisonDifferenceResponse Difference);
