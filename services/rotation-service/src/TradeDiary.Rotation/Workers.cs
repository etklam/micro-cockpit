using System.Diagnostics;
using Npgsql;

public static class RotationEngine
{
    public const string FormulaVersion = "rotation-v1";

    public static bool HasSufficientData(int sampleCount) => sampleCount >= 200;

    public static decimal? PercentChange(decimal current, decimal? previous) =>
        previous is null or 0 ? null : (current / previous.Value - 1) * 100;

    public static bool ShouldRetryInsufficientData(DateOnly? previousSourceMaxDate, DateOnly? currentSourceMaxDate) =>
        currentSourceMaxDate is not null && (previousSourceMaxDate is null || currentSourceMaxDate > previousSourceMaxDate);

    public static bool ShouldReuseBatch(string status, DateOnly? previousSourceMaxDate, DateOnly? currentSourceMaxDate) =>
        status is "completed" or "running" ||
        status == "insufficient_data" && !ShouldRetryInsufficientData(previousSourceMaxDate, currentSourceMaxDate);

    public static async Task<CalculateResponse> Calculate(NpgsqlDataSource db, Guid universeId, DateOnly snapshotDate)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var runId = Guid.NewGuid();
        var existingStatus = await ClaimBatch(connection, transaction, runId, universeId, snapshotDate);
        if (existingStatus is "completed" or "insufficient_data" or "running")
        {
            await transaction.CommitAsync();
            return new CalculateResponse(universeId, snapshotDate, existingStatus, FormulaVersion);
        }

        try
        {
            foreach (var table in new[] { "market_rotation_snapshots", "sector_breadth_snapshots", "market_state_snapshots" })
            {
                await using var delete = new NpgsqlCommand($"DELETE FROM rotation.{table} WHERE universe_id=$1 AND snapshot_date=$2", connection, transaction);
                delete.Parameters.AddWithValue(universeId); delete.Parameters.AddWithValue(snapshotDate); await delete.ExecuteNonQueryAsync();
            }

            await using (var snapshots = new NpgsqlCommand(Sql.Snapshot, connection, transaction))
            {
                snapshots.Parameters.AddWithValue(universeId); snapshots.Parameters.AddWithValue(snapshotDate); snapshots.Parameters.AddWithValue(FormulaVersion); await snapshots.ExecuteNonQueryAsync();
            }
            await using (var breadth = new NpgsqlCommand(Sql.Breadth, connection, transaction))
            {
                breadth.Parameters.AddWithValue(universeId); breadth.Parameters.AddWithValue(snapshotDate); breadth.Parameters.AddWithValue(FormulaVersion); await breadth.ExecuteNonQueryAsync();
            }
            await using (var state = new NpgsqlCommand(Sql.State, connection, transaction))
            {
                state.Parameters.AddWithValue(universeId); state.Parameters.AddWithValue(snapshotDate); state.Parameters.AddWithValue(FormulaVersion); await state.ExecuteNonQueryAsync();
            }

            await using var finish = new NpgsqlCommand("UPDATE rotation.batch_runs SET status=CASE WHEN EXISTS(SELECT 1 FROM rotation.market_rotation_snapshots WHERE universe_id=$1 AND snapshot_date=$2) AND NOT EXISTS(SELECT 1 FROM rotation.market_rotation_snapshots WHERE universe_id=$1 AND snapshot_date=$2 AND status <> 'ok') THEN 'completed' ELSE 'insufficient_data' END,source_max_date=(SELECT max(trade_date) FROM market_data_public.adjusted_daily_bars_v1 WHERE trade_date <= $2),finished_at=now() WHERE universe_id=$1 AND snapshot_date=$2 AND formula_version=$3 RETURNING status", connection, transaction);
            finish.Parameters.AddWithValue(universeId); finish.Parameters.AddWithValue(snapshotDate); finish.Parameters.AddWithValue(FormulaVersion);
            var status = (string)(await finish.ExecuteScalarAsync())!;
            await transaction.CommitAsync();
            return new CalculateResponse(universeId, snapshotDate, status, FormulaVersion);
        }
        catch (Exception error)
        {
            await transaction.RollbackAsync();
            await PersistFailure(db, runId, universeId, snapshotDate, error.Message);
            throw;
        }
    }

    private static async Task PersistFailure(NpgsqlDataSource db, Guid runId, Guid universeId, DateOnly snapshotDate, string error)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var fail = new NpgsqlCommand("""
            INSERT INTO rotation.batch_runs(id,universe_id,snapshot_date,formula_version,status,finished_at,error)
            VALUES($1,$2,$3,$4,'failed',now(),$5)
            ON CONFLICT(universe_id,snapshot_date,formula_version) DO UPDATE
            SET status='failed',finished_at=now(),error=EXCLUDED.error
            WHERE rotation.batch_runs.status <> 'running' OR rotation.batch_runs.id=$1
            """, connection, transaction);
        fail.Parameters.AddWithValue(runId);
        fail.Parameters.AddWithValue(universeId);
        fail.Parameters.AddWithValue(snapshotDate);
        fail.Parameters.AddWithValue(FormulaVersion);
        fail.Parameters.AddWithValue(error);
        await fail.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<string?> ClaimBatch(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId, Guid universeId, DateOnly snapshotDate)
    {
        await using var insert = new NpgsqlCommand("INSERT INTO rotation.batch_runs(id,universe_id,snapshot_date,formula_version,status) SELECT $1,$2,$3,$4,'running' WHERE EXISTS(SELECT 1 FROM rotation.market_rotation_universes WHERE id=$2) ON CONFLICT(universe_id,snapshot_date,formula_version) DO NOTHING RETURNING status", connection, transaction);
        insert.Parameters.AddWithValue(runId); insert.Parameters.AddWithValue(universeId); insert.Parameters.AddWithValue(snapshotDate); insert.Parameters.AddWithValue(FormulaVersion);
        if (await insert.ExecuteScalarAsync() is not null) return null;

        string status;
        DateOnly? previousSourceMaxDate;
        DateOnly? currentSourceMaxDate;
        await using (var existing = new NpgsqlCommand("SELECT status,source_max_date,(SELECT max(trade_date) FROM market_data_public.adjusted_daily_bars_v1 WHERE trade_date <= $2) FROM rotation.batch_runs WHERE universe_id=$1 AND snapshot_date=$2 AND formula_version=$3 FOR UPDATE", connection, transaction))
        {
            existing.Parameters.AddWithValue(universeId); existing.Parameters.AddWithValue(snapshotDate); existing.Parameters.AddWithValue(FormulaVersion);
            await using var reader = await existing.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new KeyNotFoundException("rotation universe not found");
            status = reader.GetString(0);
            previousSourceMaxDate = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1);
            currentSourceMaxDate = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2);
        }
        if (ShouldReuseBatch(status, previousSourceMaxDate, currentSourceMaxDate)) return status;

        await using var retry = new NpgsqlCommand("UPDATE rotation.batch_runs SET status='running',started_at=now(),finished_at=NULL,error=NULL WHERE universe_id=$1 AND snapshot_date=$2 AND formula_version=$3", connection, transaction);
        retry.Parameters.AddWithValue(universeId); retry.Parameters.AddWithValue(snapshotDate); retry.Parameters.AddWithValue(FormulaVersion); await retry.ExecuteNonQueryAsync();
        return null;
    }
}

public sealed class RotationWorkerMetrics
{
    public DateTime? LastSuccessUtc { get; private set; }
    public void MarkSuccess() => LastSuccessUtc = DateTime.UtcNow;
}

sealed class RotationWorker(NpgsqlDataSource db, IConfiguration configuration, RotationWorkerMetrics metrics, ILogger<RotationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Workers:Rotation:Enabled", true))
        {
            logger.LogInformation("Rotation worker disabled (Workers:Rotation:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Workers:Rotation:IntervalSeconds", 60)));
        var runAt = TimeOnly.TryParse(configuration["Workers:Rotation:RunAtUtc"], out var configured) ? configured : new TimeOnly(2, 0);
        DateOnly? completedForSnapshotDate = null;
        logger.LogInformation("Rotation worker starting; interval {IntervalSeconds}s; run-at UTC {RunAtUtc}.", interval.TotalSeconds, runAt);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            if (now.TimeOfDay >= runAt.ToTimeSpan())
            {
                var runId = Guid.NewGuid();
                using var scope = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId });
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var snapshotDate = await LatestPublishedTradeDate(db, stoppingToken);
                    if (snapshotDate is null)
                    {
                        logger.LogInformation("Rotation run skipped; no published market data is available.");
                    }
                    else if (completedForSnapshotDate != snapshotDate)
                    {
                        var allSucceeded = true;
                        var universes = new List<Guid>();
                        await using (var command = db.CreateCommand("SELECT id FROM rotation.market_rotation_universes ORDER BY code"))
                        await using (var reader = await command.ExecuteReaderAsync(stoppingToken))
                        {
                            while (await reader.ReadAsync(stoppingToken)) universes.Add(reader.GetGuid(0));
                        }
                        foreach (var universeId in universes)
                        {
                            try
                            {
                                var result = await RotationEngine.Calculate(db, universeId, snapshotDate.Value);
                                logger.LogInformation("Rotation universe {UniverseId} completed with status {Status}.", universeId, result.Status);
                            }
                            catch (Exception error)
                            {
                                allSucceeded = false;
                                logger.LogError(error, "Rotation universe {UniverseId} failed.", universeId);
                            }
                        }

                        if (allSucceeded)
                        {
                            completedForSnapshotDate = snapshotDate;
                            metrics.MarkSuccess();
                        }
                        logger.LogInformation("Rotation run completed for snapshot {SnapshotDate}; universes {UniverseCount}; success {Success}; duration {DurationMs}ms.", snapshotDate, universes.Count, allSucceeded, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    }
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(error, "Rotation run failed; duration {DurationMs}ms.", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task<DateOnly?> LatestPublishedTradeDate(NpgsqlDataSource db, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand("SELECT max(trade_date) FROM market_data_public.adjusted_daily_bars_v1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return null;
        return reader.GetFieldValue<DateOnly>(0);
    }
}
