using Npgsql;
using Testcontainers.PostgreSql;

public sealed class PostgresIdempotencyConcurrencyTests
{
    [Fact]
    public async Task Postgres_unique_key_allows_one_owner_for_concurrent_retries()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();

        await using var setup = new NpgsqlConnection(postgres.GetConnectionString());
        await setup.OpenAsync();
        await using (var command = new NpgsqlCommand("CREATE TABLE idempotency_keys (user_id uuid NOT NULL, operation text NOT NULL, idempotency_key text NOT NULL, PRIMARY KEY(user_id,operation,idempotency_key))", setup))
            await command.ExecuteNonQueryAsync();

        var userId = Guid.NewGuid();
        async Task<int> ReserveAsync()
        {
            await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("INSERT INTO idempotency_keys(user_id,operation,idempotency_key) VALUES($1,$2,$3) ON CONFLICT DO NOTHING", connection);
            command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue("create-diary"); command.Parameters.AddWithValue("retry-1");
            return await command.ExecuteNonQueryAsync();
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ReserveAsync()));

        Assert.Equal(1, results.Sum());
    }
}
