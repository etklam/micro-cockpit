using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class ReadinessTests
{
    [Fact]
    public async Task Ready_probe_releases_database_connections_for_the_next_probe()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("readiness_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            MaxPoolSize = 2,
            Timeout = 1,
        }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var keyPath = Path.Combine(Path.GetTempPath(), $"identity-readiness-{Guid.NewGuid():N}.pem");

        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = connectionString,
                    ["Jwt:PrivateKeyPath"] = keyPath,
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<NpgsqlDataSource>();
                    services.AddSingleton(dataSource);
                });
            });
            using var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);

            for (var attempt = 0; attempt < 4; attempt++)
            {
                using var response = await client.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            if (File.Exists(keyPath)) File.Delete(keyPath);
        }
    }
}
