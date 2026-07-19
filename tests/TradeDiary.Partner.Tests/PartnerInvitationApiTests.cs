using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class PartnerInvitationApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Invitation_lifecycle_security_and_privacy_rules()
    {
        await using var fixture = await PartnerFixture.StartAsync();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var charlie = Guid.NewGuid();

        using var aliceClient = fixture.Client(alice);
        using var bobClient = fixture.Client(bob);
        using var charlieClient = fixture.Client(charlie);

        using var create = await aliceClient.PostAsync("/internal/partners/invitations", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<InvitationCreated>(Json);
        Assert.False(string.IsNullOrWhiteSpace(created!.Code));
        Assert.True(created.ExpiresAt > DateTime.UtcNow.AddDays(6));

        var (hash, status) = await fixture.ReadInvitationAsync(created.Id);
        Assert.Equal("pending", status);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(created.Code)), hash);
        // Only the hash is stored — raw code must not round-trip from the table.
        Assert.NotEqual(Encoding.UTF8.GetBytes(created.Code), hash);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await aliceClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created.Code })).StatusCode);

        using var redeem = await bobClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created.Code });
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        var redeemed = await redeem.Content.ReadFromJsonAsync<Redeemed>(Json);
        Assert.NotEqual(Guid.Empty, redeemed!.LinkId);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await charlieClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created.Code })).StatusCode);

        var list = await aliceClient.GetFromJsonAsync<CollectionResponse<PartnerLinkView>>("/internal/partners", Json);
        var link = Assert.Single(list!.Items, x => x.Id == redeemed.LinkId);
        Assert.Equal("accepted", link.Status);
        Assert.False(link.MyShareDiaries);
        Assert.False(link.PartnerShareDiaries);
        Assert.True(link.InitiatedByMe);
        Assert.Null(link.PartnerDisplayName);
        Assert.NotNull(link.AcceptedAt);
        var acceptedAt = link.AcceptedAt!.Value;

        Assert.Equal(HttpStatusCode.NotFound, (await charlieClient.GetAsync($"/internal/partners/{redeemed.LinkId}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await charlieClient.DeleteAsync($"/internal/partners/{redeemed.LinkId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await charlieClient.PutAsJsonAsync($"/internal/partners/{redeemed.LinkId}/share-policy", new { shareDiaries = true })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await aliceClient.PutAsJsonAsync($"/internal/partners/{redeemed.LinkId}/share-policy", new { shareDiaries = true })).StatusCode);
        var afterAlice = await bobClient.GetFromJsonAsync<PartnerLinkView>($"/internal/partners/{redeemed.LinkId}/summary", Json);
        Assert.True(afterAlice!.PartnerShareDiaries);
        Assert.False(afterAlice.MyShareDiaries);

        Assert.Equal(HttpStatusCode.NoContent,
            (await bobClient.PutAsJsonAsync($"/internal/partners/{redeemed.LinkId}/share-policy", new { shareDiaries = true })).StatusCode);
        var afterBob = await aliceClient.GetFromJsonAsync<PartnerLinkView>($"/internal/partners/{redeemed.LinkId}/summary", Json);
        Assert.True(afterBob!.MyShareDiaries);
        Assert.True(afterBob.PartnerShareDiaries);

        var allowed = await bobClient.GetFromJsonAsync<Authz>($"/internal/partners/{alice}/authorization?resource=diary", Json);
        Assert.True(allowed!.Allowed);
        Assert.Equal(HttpStatusCode.NoContent,
            (await aliceClient.PutAsJsonAsync($"/internal/partners/{redeemed.LinkId}/share-policy", new { shareDiaries = false })).StatusCode);
        var denied = await bobClient.GetFromJsonAsync<Authz>($"/internal/partners/{alice}/authorization?resource=diary", Json);
        Assert.False(denied!.Allowed);

        using var create2 = await aliceClient.PostAsync("/internal/partners/invitations", null);
        var created2 = await create2.Content.ReadFromJsonAsync<InvitationCreated>(Json);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await bobClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created2!.Code })).StatusCode);

        using var create3 = await aliceClient.PostAsync("/internal/partners/invitations", null);
        var created3 = await create3.Content.ReadFromJsonAsync<InvitationCreated>(Json);
        Assert.Equal(HttpStatusCode.NoContent, (await aliceClient.DeleteAsync($"/internal/partners/invitations/{created3!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await charlieClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created3.Code })).StatusCode);

        using var create4 = await aliceClient.PostAsync("/internal/partners/invitations", null);
        var created4 = await create4.Content.ReadFromJsonAsync<InvitationCreated>(Json);
        await fixture.ExecuteAsync("UPDATE partner.partner_invitations SET expires_at = now() - interval '1 minute' WHERE id=$1", created4!.Id);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await charlieClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created4.Code })).StatusCode);

        // accepted_at is append-only: revoke does not clear it; updates leave it unchanged.
        await fixture.ExecuteAsync("UPDATE partner.partner_links SET updated_at = now() + interval '1 hour' WHERE id=$1", redeemed.LinkId);
        var acceptedStill = await fixture.ScalarAsync<DateTime>("SELECT accepted_at FROM partner.partner_links WHERE id=$1", redeemed.LinkId);
        Assert.Equal(DateTime.SpecifyKind(acceptedAt, DateTimeKind.Utc), DateTime.SpecifyKind(acceptedStill, DateTimeKind.Utc));

        Assert.Equal(HttpStatusCode.NoContent, (await aliceClient.DeleteAsync($"/internal/partners/{redeemed.LinkId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bobClient.PutAsJsonAsync($"/internal/partners/{redeemed.LinkId}/share-policy", new { shareDiaries = true })).StatusCode);
        var afterRevoke = await bobClient.GetFromJsonAsync<Authz>($"/internal/partners/{alice}/authorization?resource=diary", Json);
        Assert.False(afterRevoke!.Allowed);
        var acceptedAfterRevoke = await fixture.ScalarAsync<DateTime?>("SELECT accepted_at FROM partner.partner_links WHERE id=$1", redeemed.LinkId);
        Assert.NotNull(acceptedAfterRevoke);
    }

    [Fact]
    public async Task Display_names_degrade_when_identity_returns_null_or_fails()
    {
        await using var fixture = await PartnerFixture.StartAsync(identityMode: IdentityMode.NullNames);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        using var aliceClient = fixture.Client(alice);
        using var bobClient = fixture.Client(bob);
        using var create = await aliceClient.PostAsync("/internal/partners/invitations", null);
        var created = await create.Content.ReadFromJsonAsync<InvitationCreated>(Json);
        using var redeem = await bobClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created!.Code });
        var redeemed = await redeem.Content.ReadFromJsonAsync<Redeemed>(Json);
        var summary = await aliceClient.GetFromJsonAsync<PartnerLinkView>($"/internal/partners/{redeemed!.LinkId}/summary", Json);
        Assert.Null(summary!.PartnerDisplayName);

        await using var failFixture = await PartnerFixture.StartAsync(identityMode: IdentityMode.Fail);
        using var alice2 = failFixture.Client(alice);
        using var bob2 = failFixture.Client(bob);
        using var create2 = await alice2.PostAsync("/internal/partners/invitations", null);
        var created2 = await create2.Content.ReadFromJsonAsync<InvitationCreated>(Json);
        using var redeem2 = await bob2.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created2!.Code });
        var redeemed2 = await redeem2.Content.ReadFromJsonAsync<Redeemed>(Json);
        var summary2 = await alice2.GetFromJsonAsync<PartnerLinkView>($"/internal/partners/{redeemed2!.LinkId}/summary", Json);
        Assert.Null(summary2!.PartnerDisplayName);
    }

    [Fact]
    public async Task Legacy_pending_accept_sets_accepted_at_once_without_raw_create_endpoint()
    {
        await using var fixture = await PartnerFixture.StartAsync();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        await fixture.ExecuteAsync("INSERT INTO partner.partner_links(id,requester_user_id,partner_user_id,partner_type,status) VALUES($1,$2,$3,'human','pending')", linkId, alice, bob);
        using var aliceClient = fixture.Client(alice);
        using var bobClient = fixture.Client(bob);

        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await aliceClient.PostAsJsonAsync("/internal/partners", new { partnerUserId = bob, partnerType = "human" })).StatusCode);
        Assert.Null(await fixture.ScalarAsync<DateTime?>("SELECT accepted_at FROM partner.partner_links WHERE id=$1", linkId));
        Assert.Equal(HttpStatusCode.NoContent, (await bobClient.PostAsync($"/internal/partners/{linkId}/accept", null)).StatusCode);
        var accepted = await fixture.ScalarAsync<DateTime>("SELECT accepted_at FROM partner.partner_links WHERE id=$1", linkId);
        Assert.NotEqual(default, accepted);
        await fixture.ExecuteAsync("UPDATE partner.partner_links SET updated_at = now() + interval '2 hours' WHERE id=$1", linkId);
        var still = await fixture.ScalarAsync<DateTime>("SELECT accepted_at FROM partner.partner_links WHERE id=$1", linkId);
        Assert.Equal(DateTime.SpecifyKind(accepted, DateTimeKind.Utc), DateTime.SpecifyKind(still, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Concurrent_redeem_cannot_create_duplicate_links()
    {
        await using var fixture = await PartnerFixture.StartAsync();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var charlie = Guid.NewGuid();
        using var aliceClient = fixture.Client(alice);
        using var create = await aliceClient.PostAsync("/internal/partners/invitations", null);
        var created = await create.Content.ReadFromJsonAsync<InvitationCreated>(Json);

        using var bobClient = fixture.Client(bob);
        using var charlieClient = fixture.Client(charlie);
        var results = await Task.WhenAll(
            bobClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created!.Code }),
            charlieClient.PostAsJsonAsync("/internal/partners/invitations/redeem", new { code = created.Code }));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM partner.partner_links WHERE status='accepted'"));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM partner.partner_invitations WHERE status='redeemed'"));
    }

    private sealed record InvitationCreated(Guid Id, string Code, DateTime ExpiresAt);
    private sealed record Redeemed(Guid LinkId);
    private sealed record Authz(bool Allowed);
    private sealed record CollectionResponse<T>(List<T> Items);
    private sealed record PartnerLinkView(
        Guid Id, Guid OtherUserId, string PartnerType, string Status, DateTime CreatedAt, DateTime UpdatedAt,
        DateTime? AcceptedAt, bool InitiatedByMe, bool MyShareDiaries, bool PartnerShareDiaries, string? PartnerDisplayName);

    private enum IdentityMode { Default, NullNames, Fail }

    private sealed class PartnerFixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
        private NpgsqlConnection _setup = null!;
        private NpgsqlDataSource _dataSource = null!;
        private WebApplicationFactory<Program> _factory = null!;

        internal static async Task<PartnerFixture> StartAsync(IdentityMode identityMode = IdentityMode.Default)
        {
            var fixture = new PartnerFixture();
            await fixture._postgres.StartAsync();
            fixture._setup = new NpgsqlConnection(fixture._postgres.GetConnectionString());
            await fixture._setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            foreach (var file in new[] { "0011_partner_content_operations.sql", "0021_partner_invitations.sql", "0022_partner_link_accepted_at.sql" })
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", file)), fixture._setup)
                    .ExecuteNonQueryAsync();

            fixture._dataSource = NpgsqlDataSource.Create(fixture._postgres.GetConnectionString());
            fixture._factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<NpgsqlDataSource>();
                    services.AddSingleton(fixture._dataSource);
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuth.Scheme;
                        options.DefaultChallengeScheme = TestAuth.Scheme;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
                    services.AddHttpClient("identity").ConfigurePrimaryHttpMessageHandler(() => new StubIdentityHandler(identityMode));
                });
            });
            return fixture;
        }

        internal HttpClient Client(Guid userId)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
            return client;
        }

        internal async Task ExecuteAsync(string sql, params object[] args)
        {
            await using var command = new NpgsqlCommand(sql, _setup);
            foreach (var arg in args) command.Parameters.AddWithValue(arg);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<(byte[] Hash, string Status)> ReadInvitationAsync(Guid id)
        {
            await using var command = new NpgsqlCommand(
                "SELECT code_hash, status FROM partner.partner_invitations WHERE id=$1", _setup);
            command.Parameters.AddWithValue(id);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return ((byte[])reader[0], reader.GetString(1));
        }

        internal async Task<long> CountAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, _setup);
            return (long)(await command.ExecuteScalarAsync())!;
        }

        internal async Task<T> ScalarAsync<T>(string sql, params object[] args)
        {
            await using var command = new NpgsqlCommand(sql, _setup);
            foreach (var arg in args) command.Parameters.AddWithValue(arg);
            var value = await command.ExecuteScalarAsync();
            if (value is null or DBNull) return default!;
            return (T)value;
        }

        public async ValueTask DisposeAsync()
        {
            _factory.Dispose();
            await _dataSource.DisposeAsync();
            await _setup.DisposeAsync();
            await _postgres.DisposeAsync();
        }
    }

    private sealed class StubIdentityHandler(IdentityMode mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (mode == IdentityMode.Fail)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            // Null/blank display names must degrade to Partner placeholder in service layer.
            var body = mode == IdentityMode.NullNames
                ? """{"items":[{"userId":"00000000-0000-0000-0000-000000000001","displayName":null},{"userId":"00000000-0000-0000-0000-000000000002","displayName":"  "}]}"""
                : """{"items":[]}""";
            // Always answer with null names for any ids so degradation is exercised regardless of GUID.
            if (mode == IdentityMode.NullNames)
            {
                var ids = request.RequestUri!.Query.TrimStart('?').Split('&')
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2 && p[0] == "ids")
                    .SelectMany(p => Uri.UnescapeDataString(p[1]).Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .ToArray();
                var items = string.Join(",", ids.Select(id => $"{{\"userId\":\"{id}\",\"displayName\":null}}"));
                body = $"{{\"items\":[{items}]}}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "partner-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, id),
                new Claim("account_type", "human")
            ], Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
