using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class RegistrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("identity_registration_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SCHEMA identity;
            CREATE TABLE identity.users (
                id uuid PRIMARY KEY,
                email text NOT NULL UNIQUE,
                display_name text NOT NULL,
                timezone text NOT NULL DEFAULT 'Asia/Taipei',
                base_currency char(3) NOT NULL DEFAULT 'USD',
                role text NOT NULL DEFAULT 'user',
                account_type text NOT NULL DEFAULT 'human',
                status text NOT NULL DEFAULT 'active',
                status_version integer NOT NULL DEFAULT 1,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE identity.user_credentials (
                user_id uuid PRIMARY KEY REFERENCES identity.users(id) ON DELETE CASCADE,
                password_salt bytea NOT NULL,
                password_hash bytea NOT NULL,
                iterations integer NOT NULL
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task Public_registration_creates_user_without_registration_key()
    {
        await using var factory = CreateFactory(allowPublicRegistration: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/internal/auth/register", RegisterBody("public@example.test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.Equal("public@example.test", body?.Email);
        Assert.Equal("USD", body?.BaseCurrency);
    }

    [Fact]
    public async Task Public_registration_rejects_duplicate_email()
    {
        await using var factory = CreateFactory(allowPublicRegistration: true);
        using var client = factory.CreateClient();
        var body = RegisterBody("duplicate@example.test");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/internal/auth/register", body)).StatusCode);

        using var duplicate = await client.PostAsJsonAsync("/internal/auth/register", body);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Public_registration_validates_input()
    {
        await using var factory = CreateFactory(allowPublicRegistration: true);
        using var client = factory.CreateClient();

        using var invalidRegistration = await client.PostAsJsonAsync("/internal/auth/register", RegisterBody("not-an-email", password: "too-short"));
        using var invalidLocale = await client.PostAsJsonAsync("/internal/auth/register", RegisterBody("bad-locale@example.test", baseCurrency: "US"));

        Assert.Equal(HttpStatusCode.BadRequest, invalidRegistration.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLocale.StatusCode);
    }

    [Fact]
    public async Task Disabled_registration_is_not_found_when_no_public_flag_or_key_exists()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/internal/auth/register", RegisterBody("disabled@example.test"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Gated_registration_accepts_matching_registration_key()
    {
        await using var factory = CreateFactory(registrationKey: "local-registration-key");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/register")
        {
            Content = JsonContent.Create(RegisterBody("gated@example.test"))
        };
        request.Headers.Add("X-Registration-Key", "local-registration-key");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    [InlineData("x")]
    public async Task Gated_registration_rejects_missing_wrong_and_wrong_length_keys(string? suppliedKey)
    {
        await using var factory = CreateFactory(registrationKey: "local-registration-key");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/register")
        {
            Content = JsonContent.Create(RegisterBody($"rejected-{Guid.NewGuid():N}@example.test"))
        };
        if (suppliedKey is not null) request.Headers.Add("X-Registration-Key", suppliedKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private IdentityTestFactory CreateFactory(bool allowPublicRegistration = false, string? registrationKey = null) =>
        new(postgres.GetConnectionString(), allowPublicRegistration, registrationKey);

    private static object RegisterBody(
        string email,
        string password = "correct horse battery staple",
        string displayName = "Test User",
        string timezone = "UTC",
        string baseCurrency = "USD") => new
    {
        Email = email,
        Password = password,
        DisplayName = displayName,
        Timezone = timezone,
        BaseCurrency = baseCurrency
    };

    private sealed class IdentityTestFactory(
        string connectionString,
        bool allowPublicRegistration,
        string? registrationKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = connectionString,
                ["Auth:AllowPublicRegistration"] = allowPublicRegistration.ToString(),
                ["Auth:LocalRegistrationKey"] = registrationKey
            }));
        }
    }

    private sealed record RegisterResponse(Guid Id, string Email, string DisplayName, string Timezone, string BaseCurrency);
}
