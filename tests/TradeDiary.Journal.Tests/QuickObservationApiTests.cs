using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class QuickObservationApiTests
{
    private static readonly Guid KnownInstrumentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData("2026-07-14T01:29:59Z", "America/Los_Angeles", "18:30", "2026-07-12")]
    [InlineData("2026-07-14T01:30:00Z", "America/Los_Angeles", "18:30", "2026-07-13")]
    [InlineData("2026-03-08T09:30:00Z", "America/Los_Angeles", "02:00", "2026-03-07")]
    [InlineData("2026-03-08T10:00:00Z", "America/Los_Angeles", "02:00", "2026-03-08")]
    [InlineData("2026-11-01T08:29:59Z", "America/Los_Angeles", "01:30", "2026-10-31")]
    [InlineData("2026-11-01T08:30:00Z", "America/Los_Angeles", "01:30", "2026-11-01")]
    [InlineData("2026-11-01T09:00:00Z", "America/Los_Angeles", "01:30", "2026-11-01")]
    public void Journal_day_uses_timezone_and_rollover(string instant, string timezone, string rollover, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), JournalDay.Resolve(DateTimeOffset.Parse(instant), timezone, rollover));
    }

    [Theory]
    [InlineData("2026-07-08", "UTC", "00:00", "2026-07-08T00:00:00Z", "2026-07-09T00:00:00Z")]
    [InlineData("2026-07-08", "Asia/Taipei", "06:00", "2026-07-07T22:00:00Z", "2026-07-08T22:00:00Z")]
    [InlineData("2026-03-08", "America/Los_Angeles", "02:00", "2026-03-08T10:00:00Z", "2026-03-09T09:00:00Z")]
    [InlineData("2026-11-01", "America/Los_Angeles", "01:30", "2026-11-01T08:30:00Z", "2026-11-02T09:30:00Z")]
    public void Journal_day_utc_window_uses_the_same_rollover_and_dst_rules(
        string day, string timezone, string rollover, string expectedStart, string expectedEnd)
    {
        Assert.True(JournalDay.TryUtcWindow(
            DateOnly.Parse(day), DateOnly.Parse(day), timezone, rollover, 366,
            out var startUtc, out var endUtc));
        Assert.Equal(DateTime.Parse(expectedStart).ToUniversalTime(), startUtc);
        Assert.Equal(DateTime.Parse(expectedEnd).ToUniversalTime(), endUtc);
    }

    [Fact]
    public async Task Quick_observation_creates_appends_and_allows_owner_edit()
    {
        var now = new DateTimeOffset(2026, 7, 14, 1, 30, 0, TimeSpan.Zero);
        await using var fixture = await Fixture.StartAsync(now);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner, "America/Los_Angeles", "18:30");

        using var first = await Post(client, "first signal", "quick-1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-07-13", firstBody.GetProperty("journalDay").GetString());
        Assert.False(firstBody.GetProperty("appended").GetBoolean());
        var observationId = firstBody.GetProperty("marketObservationId").GetGuid();
        var updateId = firstBody.GetProperty("observationUpdateId").GetGuid();

        using var replay = await Post(client, "first signal", "quick-1");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(updateId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid());

        using var second = await Post(client, "changed view", "quick-2");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(secondBody.GetProperty("appended").GetBoolean());
        Assert.Equal(observationId, secondBody.GetProperty("marketObservationId").GetGuid());

        var today = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations/today");
        Assert.Equal(observationId, today.GetProperty("id").GetGuid());
        Assert.Equal(2, today.GetProperty("updates").GetArrayLength());
        Assert.Equal("first signal", today.GetProperty("updates")[0].GetProperty("content").GetString());
        Assert.Equal("changed view", today.GetProperty("updates")[1].GetProperty("content").GetString());

        using var edit = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new { content = "corrected signal" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var editBody = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(editBody.GetProperty("honestyReminderRequired").GetBoolean());

        var after = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations/today");
        var edited = after.GetProperty("updates").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == updateId);
        Assert.Equal("corrected signal", edited.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Daily_close_5xx_is_degraded_without_failing_the_today_response()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero), dailyCloseUnavailable: true);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var created = await Post(client, "Initial note", "daily-close-failure");
        var updateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();

        using var enriched = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            primarySubject = new { type = "instrument", instrumentId = KnownInstrumentId, market = "US", symbol = "AAPL", displayName = "Apple Inc." },
        });
        Assert.Equal(HttpStatusCode.OK, enriched.StatusCode);

        using var today = await client.GetAsync("/internal/market-observations/today");
        Assert.Equal(HttpStatusCode.OK, today.StatusCode);
        var body = await today.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unavailable", body.GetProperty("updates")[0].GetProperty("primarySubject").GetProperty("dailyCloseStatus").GetString());
    }

    [Fact]
    public async Task Owner_can_enrich_an_update_without_blurring_subjects_tags_or_evidence()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var created = await Post(client, "Initial note", "enrich-1");
        var updateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        var instrumentId = KnownInstrumentId;

        using var enriched = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            signal = "Breadth improved into the close",
            interpretation = "Risk appetite may be returning",
            mentalState = "patient",
            tags = new[] { "Closing session", "closing session", "breadth" },
            primarySubject = new { type = "instrument", instrumentId, market = "US", symbol = "AAPL", displayName = "Apple Inc." },
            relatedSubjects = new object[]
            {
                new { type = "broad_market", name = "US equities" },
                new { type = "theme", name = "Artificial intelligence" },
                new { type = "instrument", market = "HK", symbol = "0700", displayName = "Tencent" },
            },
            evidence = new { url = "https://example.com/market", title = "Closing breadth", quote = "Advancers led decliners." },
        });

        Assert.Equal(HttpStatusCode.OK, enriched.StatusCode);
        var today = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations/today");
        var update = today.GetProperty("updates")[0];
        Assert.Equal("Breadth improved into the close", update.GetProperty("signal").GetString());
        Assert.Equal("Risk appetite may be returning", update.GetProperty("interpretation").GetString());
        Assert.Equal("patient", update.GetProperty("mentalState").GetString());
        Assert.Equal(new[] { "breadth", "closing session" }, update.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(instrumentId, update.GetProperty("primarySubject").GetProperty("instrumentId").GetGuid());
        Assert.True(update.GetProperty("primarySubject").GetProperty("dailyCloseAvailable").GetBoolean());
        Assert.Equal("available", update.GetProperty("primarySubject").GetProperty("dailyCloseStatus").GetString());
        Assert.Equal(100m, update.GetProperty("primarySubject").GetProperty("dailyClose").GetProperty("rawClose").GetDecimal());
        Assert.Equal(50m, update.GetProperty("primarySubject").GetProperty("dailyClose").GetProperty("adjustedClose").GetDecimal());
        Assert.False(update.GetProperty("relatedSubjects")[2].GetProperty("dailyCloseAvailable").GetBoolean());
        Assert.Equal("unsupported", update.GetProperty("relatedSubjects")[2].GetProperty("dailyCloseStatus").GetString());
        Assert.Equal("https://example.com/market", update.GetProperty("evidence").GetProperty("url").GetString());

        using var unsupportedUs = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            primarySubject = new { type = "instrument", market = "US", symbol = "AAPL", displayName = "Apple Inc." },
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedUs.StatusCode);
        Assert.Contains("directory_instrument_required", await unsupportedUs.Content.ReadAsStringAsync());

        using var unknownInstrument = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            primarySubject = new { type = "instrument", instrumentId = Guid.NewGuid(), market = "US", symbol = "FAKE", displayName = "Fabricated" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknownInstrument.StatusCode);
        Assert.Contains("unknown_instrument", await unknownInstrument.Content.ReadAsStringAsync());

        using var evidenceWithoutSignal = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            evidence = new { url = "https://example.com/source", title = "Source", quote = "Context" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, evidenceWithoutSignal.StatusCode);
        Assert.Contains("evidence_requires_signal", await evidenceWithoutSignal.Content.ReadAsStringAsync());

        using var invalidEvidence = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            signal = "A signal",
            evidence = new { url = "file:///tmp/source", title = "Local", quote = "Not allowed" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEvidence.StatusCode);
        Assert.Contains("invalid_evidence_url", await invalidEvidence.Content.ReadAsStringAsync());

        using var other = fixture.Client(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new { content = "stolen", signal = "private" })).StatusCode);
    }

    [Fact]
    public async Task Owner_can_search_current_observation_updates_with_filters_and_stable_pagination()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);

        using var first = await Post(client, "Semiconductor breadth improved", "search-1");
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var enrich = await client.PutAsJsonAsync($"/internal/observation-updates/{firstId}", new
        {
            content = "Semiconductor breadth improved",
            signal = "Advancers led decliners",
            tags = new[] { "breadth" },
            primarySubject = new { type = "instrument", instrumentId = KnownInstrumentId, market = "US", symbol = "AAPL", displayName = "Apple" },
            relatedSubjects = new object[] { new { type = "theme", name = "AI" }, new { type = "instrument", market = "HK", symbol = "0700", displayName = "Tencent" } },
        });
        Assert.Equal(HttpStatusCode.OK, enrich.StatusCode);

        fixture.SetNow(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        using var second = await Post(client, "Dollar strengthened", "search-2");
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var secondEnrich = await client.PutAsJsonAsync($"/internal/observation-updates/{secondId}", new
        {
            content = "Dollar strengthened",
            primarySubject = new { type = "broad_market", name = "US macro" },
            tags = new[] { "closing session" },
        });
        Assert.Equal(HttpStatusCode.OK, secondEnrich.StatusCode);

        using var other = fixture.Client(Guid.NewGuid());
        using var privateWrite = await Post(other, "Private author note", "search-private");
        Assert.Equal(HttpStatusCode.Created, privateWrite.StatusCode);

        var filtered = await client.GetFromJsonAsync<JsonElement>($"/internal/market-observations?from=2026-07-16&to=2026-07-16&query=semiconductor&subjectType=theme&subject=AI&instrumentId={KnownInstrumentId}&tag=breadth&author=current&limit=10");
        Assert.Equal(1, filtered.GetProperty("items").GetArrayLength());
        Assert.Equal(firstId, filtered.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        Assert.Equal(owner, filtered.GetProperty("items")[0].GetProperty("authorId").GetGuid());

        var page1 = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations?limit=1");
        Assert.Equal(secondId, page1.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        var cursor = Uri.EscapeDataString(page1.GetProperty("nextCursor").GetString()!);
        var page2 = await client.GetFromJsonAsync<JsonElement>($"/internal/market-observations?limit=1&cursor={cursor}");
        Assert.Equal(firstId, page2.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, page2.GetProperty("nextCursor").ValueKind);

        var byInstrument = await client.GetFromJsonAsync<JsonElement>($"/internal/market-observations?instrumentId={KnownInstrumentId}&author={owner}");
        Assert.Equal(firstId, byInstrument.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        var manualInstrument = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations?market=HK&symbol=0700");
        Assert.Equal(firstId, manualInstrument.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        var hidden = await client.GetFromJsonAsync<JsonElement>($"/internal/market-observations?author={Guid.NewGuid()}");
        Assert.Equal(0, hidden.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/market-observations?cursor=invalid")).StatusCode);

        var days = await client.GetFromJsonAsync<JsonElement>("/internal/market-observation-day-summary?from=2026-07-01&to=2026-07-31");
        Assert.Equal(2, days.GetProperty("items").GetArrayLength());
        Assert.Equal(1, days.GetProperty("items")[0].GetProperty("updateCount").GetInt64());
        Assert.Equal(0, days.GetProperty("items")[0].GetProperty("readyForReviewCount").GetInt64());
    }

    [Fact]
    public async Task Owner_can_optionally_create_an_active_expectation_from_an_observation_update()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = await Fixture.StartAsync(now);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "expectation-parent");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/observation-updates/{updateId}/expectations")
        {
            Content = JsonContent.Create(new
            {
                expectedBehavior = "Breadth should remain above 60%",
                deadline = "2026-07-17T20:00:00Z",
                invalidationCondition = "Breadth closes below 45%",
                confidence = "medium",
                market = "US",
            }),
        };
        request.Headers.Add("Idempotency-Key", "expectation-1");
        using var created = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(updateId, body.GetProperty("observationUpdateId").GetGuid());
        Assert.Equal("Breadth should remain above 60%", body.GetProperty("expectedBehavior").GetString());
        Assert.Equal("medium", body.GetProperty("confidence").GetString());
        Assert.Equal("active", body.GetProperty("readiness").GetString());
        Assert.False(body.GetProperty("deadlineElapsed").GetBoolean());

        var expectations = await client.GetFromJsonAsync<JsonElement>($"/internal/expectations?observationUpdateId={updateId}");
        Assert.Single(expectations.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Expectation_creation_rejects_unsupported_market_trading_day_preset_and_hides_cross_owner_access()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = await Fixture.StartAsync(now);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "expectation-parent-2");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();

        using var unsupportedMarket = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Nikkei should hold support",
            deadlinePreset = "next_trading_day",
            invalidationCondition = "Closes below support",
            confidence = "low",
            market = "JP",
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedMarket.StatusCode);
        Assert.Contains("trading_day_preset_unavailable", await unsupportedMarket.Content.ReadAsStringAsync());

        using var other = fixture.Client(Guid.NewGuid());
        using var stolen = await other.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "stolen",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "n/a",
            confidence = "low",
            market = "US",
        });
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    [Fact]
    public async Task Owner_can_invalidate_an_expectation_early_and_editing_an_elapsed_deadline_shows_honesty_reminder()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = await Fixture.StartAsync(now);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "expectation-parent-3");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();

        using var created = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Breadth should remain above 60%",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "Breadth closes below 45%",
            confidence = "medium",
            market = "US",
        });
        var expectationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var invalidated = await client.PostAsync($"/internal/expectations/{expectationId}/invalidate", null);
        Assert.Equal(HttpStatusCode.OK, invalidated.StatusCode);
        var invalidatedBody = await invalidated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready_for_review", invalidatedBody.GetProperty("readiness").GetString());
        Assert.NotEqual(JsonValueKind.Null, invalidatedBody.GetProperty("invalidatedAt").ValueKind);

        fixture.SetNow(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));
        using var edit = await client.PutAsJsonAsync($"/internal/expectations/{expectationId}", new
        {
            expectedBehavior = "Breadth should remain above 60%",
            deadline = "2026-07-21T20:00:00Z",
            invalidationCondition = "Breadth closes below 45%",
            confidence = "high",
            market = "US",
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var editBody = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(editBody.GetProperty("honestyReminderRequired").GetBoolean());
        Assert.Equal("high", editBody.GetProperty("confidence").GetString());

        var days = await client.GetFromJsonAsync<JsonElement>("/internal/market-observation-day-summary?from=2026-07-14&to=2026-07-14");
        Assert.Equal(1, days.GetProperty("items")[0].GetProperty("readyForReviewCount").GetInt64());
    }

    [Fact]
    public async Task Owner_can_review_a_ready_expectation_with_system_and_custom_reasoning_labels()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "review-parent");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var created = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Breadth should remain above 60%",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "Breadth closes below 45%",
            confidence = "medium",
            market = "US",
        });
        var expectationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var tooEarly = await client.PutAsJsonAsync($"/internal/expectations/{expectationId}/review", new
        {
            outcome = "confirmed",
            reasoningQuality = "sound",
        });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        await client.PostAsync($"/internal/expectations/{expectationId}/invalidate", null);

        var available = await client.GetFromJsonAsync<JsonElement>("/internal/reasoning-labels");
        Assert.Equal(12, available.GetProperty("items").GetArrayLength());
        using var custom = await client.PostAsJsonAsync("/internal/reasoning-labels", new { kind = "issue", name = "Chased the opening move" });
        Assert.Equal(HttpStatusCode.Created, custom.StatusCode);
        var customId = (await custom.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var missingExplanation = await client.PutAsJsonAsync($"/internal/expectations/{expectationId}/review", new
        {
            outcome = "partially_confirmed",
            reasoningQuality = "mixed",
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingExplanation.StatusCode);
        Assert.Contains("explanation_required", await missingExplanation.Content.ReadAsStringAsync());

        using var saved = await client.PutAsJsonAsync($"/internal/expectations/{expectationId}/review", new
        {
            outcome = "partially_confirmed",
            reasoningQuality = "mixed",
            explanation = "Breadth held, but leadership narrowed.",
            systemIssueKeys = new[] { "insufficient_evidence" },
            systemStrengthKeys = new[] { "clear_invalidation_condition" },
            customLabelIds = new[] { customId },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var review = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, review.GetProperty("labels").GetArrayLength());

        var expectation = await client.GetFromJsonAsync<JsonElement>($"/internal/expectations/{expectationId}");
        Assert.Equal("reviewed", expectation.GetProperty("readiness").GetString());
        using var renamed = await client.PutAsJsonAsync($"/internal/reasoning-labels/{customId}", new { kind = "issue", name = "Opening move chased" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var afterRename = await client.GetFromJsonAsync<JsonElement>($"/internal/expectations/{expectationId}/review");
        Assert.Contains(afterRename.GetProperty("labels").EnumerateArray(), label => label.GetProperty("name").GetString() == "Opening move chased");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/internal/reasoning-labels/{customId}")).StatusCode);
        var afterDelete = await client.GetFromJsonAsync<JsonElement>($"/internal/expectations/{expectationId}/review");
        Assert.DoesNotContain(afterDelete.GetProperty("labels").EnumerateArray(), label => label.GetProperty("name").GetString() == "Opening move chased");
        Assert.DoesNotContain((await client.GetFromJsonAsync<JsonElement>("/internal/reasoning-labels")).GetProperty("items").EnumerateArray(),
            label => label.GetProperty("id").ValueKind != JsonValueKind.Null && label.GetProperty("id").GetGuid() == customId);

        using var other = fixture.Client(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/internal/expectations/{expectationId}/review")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/internal/expectations/{expectationId}/review", new
        {
            outcome = "confirmed",
            reasoningQuality = "sound",
        })).StatusCode);
    }

    [Fact]
    public async Task Guided_review_context_reads_the_exact_owned_source_beyond_history_pagination()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var source = await Post(client, "Original breadth note", "review-context-source");
        var sourceBody = await source.Content.ReadFromJsonAsync<JsonElement>();
        var observationId = sourceBody.GetProperty("marketObservationId").GetGuid();
        var updateId = sourceBody.GetProperty("observationUpdateId").GetGuid();
        await fixture.SetUpdateRecordedAt(updateId, new DateTime(2026, 7, 14, 1, 0, 0, DateTimeKind.Utc));
        using var enriched = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Original breadth note",
            signal = "Advancers exceeded decliners",
            interpretation = "Participation may be broadening",
            evidence = new { url = "https://example.com/breadth", title = "Closing breadth", quote = "Advancers led." },
        });
        Assert.Equal(HttpStatusCode.OK, enriched.StatusCode);

        for (var index = 0; index < 21; index++)
            using (await Post(client, $"Later update {index}", $"review-context-later-{index}")) { }

        var firstPage = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations?from=2026-07-14&to=2026-07-14&limit=20");
        Assert.DoesNotContain(firstPage.GetProperty("items").EnumerateArray(), item => item.GetProperty("update").GetProperty("id").GetGuid() == updateId);

        using var expectation = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Breadth should remain above 60%",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "Breadth closes below 45%",
            confidence = "medium",
            market = "US",
        });
        var expectationId = (await expectation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var decision = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/action-decisions", new
        {
            intent = "avoid_trade",
            reason = "Wait for broader confirmation.",
            expectationId,
            executionReview = "followed",
        });
        var decisionId = (await decision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var trade = await client.PostAsJsonAsync($"/internal/action-decisions/{decisionId}/trades", new
        {
            symbol = "SPY",
            side = "sell",
            quantity = 1,
            price = 620,
            currency = "USD",
            executedAt = "2026-07-14T15:00:00Z",
            note = "Reduced exposure.",
        });
        Assert.Equal(HttpStatusCode.Created, trade.StatusCode);

        var context = await client.GetFromJsonAsync<JsonElement>($"/internal/expectations/{expectationId}/review-context");
        Assert.Equal("available", context.GetProperty("availability").GetString());
        Assert.Empty(context.GetProperty("unavailableContext").EnumerateArray());
        Assert.Equal(observationId, context.GetProperty("marketObservation").GetProperty("id").GetGuid());
        Assert.Equal(updateId, context.GetProperty("observationUpdate").GetProperty("id").GetGuid());
        Assert.Equal("Advancers exceeded decliners", context.GetProperty("observationUpdate").GetProperty("signal").GetString());
        Assert.Equal("Participation may be broadening", context.GetProperty("observationUpdate").GetProperty("interpretation").GetString());
        Assert.Equal("https://example.com/breadth", context.GetProperty("observationUpdate").GetProperty("evidence").GetProperty("url").GetString());
        var retainedDecision = Assert.Single(context.GetProperty("actionDecisions").EnumerateArray());
        Assert.Equal("followed", retainedDecision.GetProperty("decision").GetProperty("executionReview").GetString());
        Assert.Equal("SPY", Assert.Single(retainedDecision.GetProperty("trades").EnumerateArray()).GetProperty("symbol").GetString());

        using var other = fixture.Client(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/internal/expectations/{expectationId}/review-context")).StatusCode);

        await fixture.SoftDeleteUpdate(updateId);
        var partial = await client.GetFromJsonAsync<JsonElement>($"/internal/expectations/{expectationId}/review-context");
        Assert.Equal("partial", partial.GetProperty("availability").GetString());
        Assert.Equal(JsonValueKind.Null, partial.GetProperty("observationUpdate").ValueKind);
        Assert.Empty(partial.GetProperty("actionDecisions").EnumerateArray());
        Assert.Contains(partial.GetProperty("unavailableContext").EnumerateArray(), item => item.GetString() == "observation_update");
        Assert.DoesNotContain("Later update", partial.ToString());
    }

    [Fact]
    public async Task Owner_can_record_an_action_decision_execution_review_and_optional_trade_evidence()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "decision-parent");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var expectation = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Breadth should remain above 60%",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "Breadth closes below 45%",
            confidence = "medium",
            market = "US",
        });
        var expectationId = (await expectation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var created = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/action-decisions", new
        {
            intent = "avoid_trade",
            reason = "Wait for breadth confirmation.",
            expectationId,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var decision = await created.Content.ReadFromJsonAsync<JsonElement>();
        var decisionId = decision.GetProperty("id").GetGuid();
        Assert.Equal("avoid_trade", decision.GetProperty("intent").GetString());
        Assert.Equal(expectationId, decision.GetProperty("expectationId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, decision.GetProperty("recordedAt").ValueKind);
        Assert.False(decision.TryGetProperty("outcome", out _));
        Assert.False(decision.TryGetProperty("pnl", out _));

        using var edited = await client.PutAsJsonAsync($"/internal/action-decisions/{decisionId}", new
        {
            intent = "avoid_trade",
            reason = "Waited for confirmation; no setup appeared.",
            expectationId,
            executionReview = "followed",
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editBody = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(editBody.GetProperty("honestyReminderRequired").GetBoolean());
        Assert.Equal("followed", editBody.GetProperty("executionReview").GetString());

        using var trade = await client.PostAsJsonAsync($"/internal/action-decisions/{decisionId}/trades", new
        {
            symbol = " aapl ",
            side = "buy",
            quantity = 10,
            price = 210.25m,
            currency = "usd",
            executedAt = "2026-07-14T13:00:00Z",
            note = "Small evidence-only action",
        });
        Assert.Equal(HttpStatusCode.Created, trade.StatusCode);
        var tradeBody = await trade.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AAPL", tradeBody.GetProperty("symbol").GetString());
        Assert.False(tradeBody.TryGetProperty("position", out _));
        Assert.False(tradeBody.TryGetProperty("costBasis", out _));

        using var other = fixture.Client(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/internal/action-decisions/{decisionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/internal/action-decisions/{decisionId}", new
        {
            intent = "trade",
            reason = "stolen",
        })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/internal/action-decisions/{decisionId}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/internal/action-decisions/{decisionId}/trades")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Owner_can_manage_an_instrument_watchlist_and_short_note()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/internal/watchlist/{KnownInstrumentId}", new { note = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/internal/watchlist/{KnownInstrumentId}", new { note = new string('x', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/internal/watchlist/{KnownInstrumentId}", new { note = "Track margin stabilization." })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/internal/watchlist/{KnownInstrumentId}", new { note = "Duplicate should be idempotent." })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/internal/watchlist/{Guid.NewGuid()}", new { note = "Unknown instrument." })).StatusCode);

        using var note = await client.PutAsJsonAsync($"/internal/watchlist/{KnownInstrumentId}/note", new { note = " Watch while margins stabilize. " });
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);
        Assert.Equal("Watch while margins stabilize.", (await note.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("note").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/internal/watchlist/{KnownInstrumentId}/note", new { note = new string('x', 501) })).StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/internal/watchlist");
        Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(KnownInstrumentId, list.GetProperty("items")[0].GetProperty("instrumentId").GetGuid());
        Assert.Equal("Watch while margins stabilize.", list.GetProperty("items")[0].GetProperty("note").GetString());
        using var other = fixture.Client(Guid.NewGuid());
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/internal/watchlist")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/internal/watchlist/{KnownInstrumentId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/internal/watchlist/{KnownInstrumentId}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/internal/watchlist")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task External_ingestion_can_read_the_expiring_tracked_us_instrument_set()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Apple breadth improved", "tracked-parent");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var enriched = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Apple breadth improved",
            primarySubject = new { type = "instrument", instrumentId = KnownInstrumentId, market = "US", symbol = "AAPL", displayName = "Apple Inc." },
        });
        Assert.Equal(HttpStatusCode.OK, enriched.StatusCode);

        using var noKey = fixture.RawClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await noKey.GetAsync("/internal/v1/tracked-us-instruments")).StatusCode);
        using var ingestion = fixture.ServiceClient();
        var recent = await ingestion.GetFromJsonAsync<JsonElement>("/internal/v1/tracked-us-instruments");
        Assert.Equal(1, recent.GetProperty("contractVersion").GetInt32());
        Assert.Equal(KnownInstrumentId, recent.GetProperty("items")[0].GetProperty("instrumentId").GetGuid());

        await fixture.SetUpdateRecordedAt(updateId, new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        Assert.Empty((await ingestion.GetFromJsonAsync<JsonElement>("/internal/v1/tracked-us-instruments")).GetProperty("items").EnumerateArray());

        using var expectation = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Apple holds its breakout",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "Apple closes below support",
            confidence = "medium",
            market = "US",
        });
        Assert.Equal(HttpStatusCode.Created, expectation.StatusCode);
        Assert.Single((await ingestion.GetFromJsonAsync<JsonElement>("/internal/v1/tracked-us-instruments")).GetProperty("items").EnumerateArray());

        fixture.SetNow(new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));
        Assert.Empty((await ingestion.GetFromJsonAsync<JsonElement>("/internal/v1/tracked-us-instruments")).GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/internal/watchlist/{KnownInstrumentId}", new { note = "Keep tracking the instrument." })).StatusCode);
        Assert.Single((await ingestion.GetFromJsonAsync<JsonElement>("/internal/v1/tracked-us-instruments")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Pattern_review_has_explicit_boundaries_evidence_and_manual_principle_selection()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "patterns-parent");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var created = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Breadth should remain above 60%",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "Breadth closes below 45%",
            confidence = "medium",
            market = "US",
        });
        var expectationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await client.PostAsync($"/internal/expectations/{expectationId}/invalidate", null);
        using var review = await client.PutAsJsonAsync($"/internal/expectations/{expectationId}/review", new
        {
            outcome = "confirmed",
            reasoningQuality = "sound",
            systemIssueKeys = new[] { "insufficient_evidence" },
        });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        await fixture.SetReviewCreatedAt(expectationId, new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/internal/confirmed-patterns", new { kind = "issue", key = "insufficient_evidence" })).StatusCode);

        using var secondObservation = await Post(client, "Participation remained narrow", "patterns-parent-2");
        var secondUpdateId = (await secondObservation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var secondExpectation = await client.PostAsJsonAsync($"/internal/observation-updates/{secondUpdateId}/expectations", new
        {
            expectedBehavior = "Participation should broaden beyond megacaps",
            deadline = "2026-07-18T20:00:00Z",
            invalidationCondition = "Participation remains concentrated",
            confidence = "medium",
            market = "US",
        });
        var secondExpectationId = (await secondExpectation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await client.PostAsync($"/internal/expectations/{secondExpectationId}/invalidate", null);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/internal/expectations/{secondExpectationId}/review", new
        {
            outcome = "invalidated",
            reasoningQuality = "mixed",
            systemIssueKeys = new[] { "insufficient_evidence" },
        })).StatusCode);
        await fixture.SetReviewCreatedAt(secondExpectationId, new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc));

        var weekly = await client.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=weekly");
        Assert.Equal(2, weekly.GetProperty("reviewedExpectationCount").GetInt64());
        var label = weekly.GetProperty("labels").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "insufficient_evidence");
        Assert.Equal(2, label.GetProperty("count").GetInt64());
        Assert.Equal(2, label.GetProperty("denominator").GetInt64());
        Assert.Equal("2026-07-08T00:00:00Z", label.GetProperty("firstSeen").GetDateTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
        Assert.Equal("2026-07-14T00:00:00Z", label.GetProperty("mostRecent").GetDateTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
        var evidence = label.GetProperty("evidence").EnumerateArray().Single(item => item.GetProperty("expectationId").GetGuid() == expectationId);
        Assert.Equal("Breadth should remain above 60%", evidence.GetProperty("expectedBehavior").GetString());
        Assert.Equal("Breadth is improving", evidence.GetProperty("observationExcerpt").GetString());
        Assert.Equal("confirmed", evidence.GetProperty("outcome").GetString());
        Assert.Equal("sound", evidence.GetProperty("reasoningQuality").GetString());
        Assert.Equal($"/review?expectationId={expectationId}", evidence.GetProperty("url").GetString());

        using var confirmed = await client.PostAsJsonAsync("/internal/confirmed-patterns", new { kind = "issue", key = "insufficient_evidence" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmedPattern = await confirmed.Content.ReadFromJsonAsync<JsonElement>();
        var confirmedPatternId = confirmedPattern.GetProperty("id").GetGuid();
        Assert.Equal("Insufficient evidence", confirmedPattern.GetProperty("name").GetString());
        var confirmedAgain = await (await client.PostAsJsonAsync("/internal/confirmed-patterns", new { kind = "issue", key = "insufficient_evidence" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(confirmedPatternId, confirmedAgain.GetProperty("id").GetGuid());
        Assert.Equal(1, await fixture.PatternStateCount(confirmedPatternId, "confirmed"));
        var afterConfirmation = await client.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=weekly");
        var confirmedLabel = afterConfirmation.GetProperty("labels").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "insufficient_evidence");
        Assert.Equal(confirmedPatternId, confirmedLabel.GetProperty("confirmedPatternId").GetGuid());
        Assert.True(confirmedLabel.GetProperty("patternIsConfirmed").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/internal/expectations/{expectationId}/review")).StatusCode);

        using var intruder = fixture.Client(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.DeleteAsync($"/internal/confirmed-patterns/{confirmedPatternId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/internal/confirmed-patterns/{confirmedPatternId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/internal/confirmed-patterns/{confirmedPatternId}")).StatusCode);
        var afterUnconfirm = await client.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=weekly");
        var unconfirmedLabel = afterUnconfirm.GetProperty("labels").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "insufficient_evidence");
        Assert.False(unconfirmedLabel.GetProperty("patternIsConfirmed").GetBoolean());
        Assert.Equal(2, unconfirmedLabel.GetProperty("evidence").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/internal/discipline-principles", new
        {
            content = "Must not be sourced while unconfirmed.", confirmedPatternId,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/internal/expectations/{expectationId}/review")).StatusCode);

        await fixture.SeedReview(owner, new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        var reconfirmed = await (await client.PostAsJsonAsync("/internal/confirmed-patterns", new { kind = "issue", key = "insufficient_evidence" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(confirmedPatternId, reconfirmed.GetProperty("id").GetGuid());
        Assert.True(reconfirmed.GetProperty("isConfirmed").GetBoolean());
        Assert.True(reconfirmed.GetProperty("confirmedAt").GetDateTime() >= reconfirmed.GetProperty("firstConfirmedAt").GetDateTime());
        Assert.Equal(JsonValueKind.Null, reconfirmed.GetProperty("unconfirmedAt").ValueKind);
        Assert.Equal(2, await fixture.PatternStateCount(confirmedPatternId, "confirmed"));
        Assert.Equal(1, await fixture.PatternStateCount(confirmedPatternId, "unconfirmed"));
        Assert.Equal(3, await fixture.PatternEvidenceCount(confirmedPatternId));
        await client.PostAsJsonAsync("/internal/confirmed-patterns", new { kind = "issue", key = "insufficient_evidence" });
        Assert.Equal(2, await fixture.PatternStateCount(confirmedPatternId, "confirmed"));
        Assert.Equal(3, await fixture.PatternEvidenceCount(confirmedPatternId));
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/internal/expectations/{expectationId}/review")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/internal/expectations/{expectationId}/review", new
        {
            outcome = "confirmed",
            reasoningQuality = "sound",
            systemIssueKeys = Array.Empty<string>(),
        })).StatusCode);
        var afterSourceEdit = await client.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=weekly");
        var durablePattern = afterSourceEdit.GetProperty("labels").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "insufficient_evidence");
        Assert.Equal(2, durablePattern.GetProperty("count").GetInt64());
        Assert.Contains(durablePattern.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("expectationId").GetGuid() == expectationId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/internal/expectations/{expectationId}/review")).StatusCode);

        var boundary = await client.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=custom&from=2026-07-08&to=2026-07-08");
        Assert.Equal(1, boundary.GetProperty("reviewedExpectationCount").GetInt64());
        var empty = await client.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=custom&from=2026-07-09&to=2026-07-09");
        Assert.Equal(0, empty.GetProperty("reviewedExpectationCount").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/pattern-review?range=custom&from=2026-07-10&to=2026-07-09")).StatusCode);

        var first = await (await client.PostAsJsonAsync("/internal/discipline-principles", new { content = "Wait for invalidation.", confirmedPatternId })).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostAsJsonAsync("/internal/discipline-principles", new { content = "Separate signal from interpretation." })).Content.ReadFromJsonAsync<JsonElement>();
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();
        Assert.Equal(confirmedPatternId, first.GetProperty("confirmedPatternId").GetGuid());
        Assert.Equal("Insufficient evidence", first.GetProperty("confirmedPatternLabel").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/internal/discipline-principles/{firstId}/select", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/internal/discipline-principles/{secondId}/select", null)).StatusCode);
        var principles = await client.GetFromJsonAsync<JsonElement>("/internal/discipline-principles");
        Assert.Single(principles.GetProperty("items").EnumerateArray(), item => item.GetProperty("selectedForToday").GetBoolean());
        Assert.Equal("active", principles.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == firstId).GetProperty("status").GetString());
        Assert.Equal(secondId, (await client.GetFromJsonAsync<JsonElement>("/internal/discipline-principles/today")).GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/internal/discipline-principles/{secondId}", new
        {
            content = "Separate signal from interpretation.",
            status = "disabled",
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/internal/discipline-principles/today")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/internal/discipline-principles/{firstId}", new
        {
            content = "Wait for invalidation.",
            status = "archived",
        })).StatusCode);

        using var other = fixture.Client(Guid.NewGuid());
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=monthly")).GetProperty("reviewedExpectationCount").GetInt64());
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/internal/discipline-principles")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/internal/confirmed-patterns", new { kind = "issue", key = "insufficient_evidence" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/internal/discipline-principles", new { content = "Stolen pattern.", confirmedPatternId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/internal/discipline-principles/{secondId}/select", null)).StatusCode);
    }

    [Fact]
    public async Task Pattern_review_uses_each_owners_journal_day_window_for_counts_and_evidence()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 8, 21, 59, 0, TimeSpan.Zero));
        var taipeiOwner = Guid.NewGuid();
        var losAngelesOwner = Guid.NewGuid();
        var foldOwner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        using var taipei = fixture.Client(taipeiOwner, "Asia/Taipei", "06:00");
        using var losAngeles = fixture.Client(losAngelesOwner, "America/Los_Angeles", "02:00");
        using var fold = fixture.Client(foldOwner, "America/Los_Angeles", "01:30");

        await fixture.SeedReview(taipeiOwner, DateTime.Parse("2026-07-07T21:59:59Z").ToUniversalTime());
        await fixture.SeedReview(taipeiOwner, DateTime.Parse("2026-07-07T22:00:00Z").ToUniversalTime());
        await fixture.SeedReview(taipeiOwner, DateTime.Parse("2026-07-08T21:59:59Z").ToUniversalTime());
        await fixture.SeedReview(taipeiOwner, DateTime.Parse("2026-07-08T22:00:00Z").ToUniversalTime());
        await fixture.SeedReview(otherOwner, DateTime.Parse("2026-07-08T12:00:00Z").ToUniversalTime());

        var taipeiDay = await taipei.GetFromJsonAsync<JsonElement>(
            "/internal/pattern-review?range=custom&from=2026-07-08&to=2026-07-08");
        Assert.Equal(2, taipeiDay.GetProperty("reviewedExpectationCount").GetInt32());
        var taipeiLabel = taipeiDay.GetProperty("labels").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "insufficient_evidence");
        Assert.Equal(2, taipeiLabel.GetProperty("count").GetInt32());
        Assert.All(taipeiLabel.GetProperty("evidence").EnumerateArray(), item =>
        {
            var reviewedAt = item.GetProperty("reviewedAt").GetDateTime();
            Assert.True(reviewedAt >= DateTime.Parse("2026-07-07T22:00:00Z").ToUniversalTime());
            Assert.True(reviewedAt < DateTime.Parse("2026-07-08T22:00:00Z").ToUniversalTime());
        });

        var weekly = await taipei.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=weekly");
        Assert.Equal("2026-07-02", weekly.GetProperty("from").GetString());
        Assert.Equal("2026-07-08", weekly.GetProperty("to").GetString());
        var weeklyTrend = weekly.GetProperty("labels").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "insufficient_evidence").GetProperty("trend");
        Assert.Equal("2026-06-25", weeklyTrend.GetProperty("previous").GetProperty("from").GetString());
        Assert.Equal("2026-07-01", weeklyTrend.GetProperty("previous").GetProperty("to").GetString());
        var monthly = await taipei.GetFromJsonAsync<JsonElement>("/internal/pattern-review?range=monthly");
        Assert.Equal("2026-07-01", monthly.GetProperty("from").GetString());
        Assert.Equal("2026-07-08", monthly.GetProperty("to").GetString());
        var monthlyTrend = monthly.GetProperty("labels").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "insufficient_evidence").GetProperty("trend");
        Assert.Equal("2026-06-23", monthlyTrend.GetProperty("previous").GetProperty("from").GetString());
        Assert.Equal("2026-06-30", monthlyTrend.GetProperty("previous").GetProperty("to").GetString());

        await fixture.SeedReview(losAngelesOwner, DateTime.Parse("2026-03-08T09:59:59Z").ToUniversalTime());
        await fixture.SeedReview(losAngelesOwner, DateTime.Parse("2026-03-08T10:00:00Z").ToUniversalTime());
        await fixture.SeedReview(losAngelesOwner, DateTime.Parse("2026-03-09T08:59:59Z").ToUniversalTime());
        await fixture.SeedReview(losAngelesOwner, DateTime.Parse("2026-03-09T09:00:00Z").ToUniversalTime());
        var dstDay = await losAngeles.GetFromJsonAsync<JsonElement>(
            "/internal/pattern-review?range=custom&from=2026-03-08&to=2026-03-08");
        Assert.Equal(2, dstDay.GetProperty("reviewedExpectationCount").GetInt32());

        await fixture.SeedReview(foldOwner, DateTime.Parse("2026-11-01T08:29:59Z").ToUniversalTime());
        await fixture.SeedReview(foldOwner, DateTime.Parse("2026-11-01T08:30:00Z").ToUniversalTime());
        await fixture.SeedReview(foldOwner, DateTime.Parse("2026-11-02T09:29:59Z").ToUniversalTime());
        await fixture.SeedReview(foldOwner, DateTime.Parse("2026-11-02T09:30:00Z").ToUniversalTime());
        var foldDay = await fold.GetFromJsonAsync<JsonElement>(
            "/internal/pattern-review?range=custom&from=2026-11-01&to=2026-11-01");
        Assert.Equal(2, foldDay.GetProperty("reviewedExpectationCount").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await taipei.GetAsync(
            "/internal/pattern-review?range=custom&from=2025-01-01&to=2026-01-02")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await taipei.GetAsync(
            "/internal/pattern-review?range=custom&from=9999-12-31&to=9999-12-31")).StatusCode);
        using var invalidTimezone = fixture.Client(Guid.NewGuid(), "Unsupported/Zone", "00:00");
        Assert.Equal(HttpStatusCode.BadRequest, (await invalidTimezone.GetAsync(
            "/internal/pattern-review?range=weekly")).StatusCode);
    }

    [Fact]
    public async Task Pattern_review_trends_compare_adjacent_owner_only_windows_with_explicit_support()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        using var client = fixture.Client(owner);

        for (var day = 1; day <= 5; day++)
            await fixture.SeedReview(owner, new DateTime(2026, 7, day, 12, 0, 0, DateTimeKind.Utc), day == 1);
        for (var day = 8; day <= 12; day++)
            await fixture.SeedReview(owner, new DateTime(2026, 7, day, 12, 0, 0, DateTimeKind.Utc), day <= 10);
        await fixture.SeedReview(otherOwner, new DateTime(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc));

        var review = await client.GetFromJsonAsync<JsonElement>(
            "/internal/pattern-review?range=custom&from=2026-07-08&to=2026-07-14");
        Assert.Equal(5, review.GetProperty("reviewedExpectationCount").GetInt32());
        var trend = review.GetProperty("labels").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "insufficient_evidence").GetProperty("trend");
        Assert.Equal("supported", trend.GetProperty("status").GetString());
        Assert.Equal("higher", trend.GetProperty("direction").GetString());
        var current = trend.GetProperty("current");
        Assert.Equal("2026-07-08", current.GetProperty("from").GetString());
        Assert.Equal("2026-07-14", current.GetProperty("to").GetString());
        Assert.Equal(3, current.GetProperty("occurrenceCount").GetInt32());
        Assert.Equal(5, current.GetProperty("reviewedExpectationCount").GetInt32());
        Assert.Equal(3, current.GetProperty("evidence").GetArrayLength());
        Assert.All(current.GetProperty("evidence").EnumerateArray(), item =>
            Assert.StartsWith("/review?expectationId=", item.GetProperty("url").GetString()));
        var previous = trend.GetProperty("previous");
        Assert.Equal("2026-07-01", previous.GetProperty("from").GetString());
        Assert.Equal("2026-07-07", previous.GetProperty("to").GetString());
        Assert.Equal(1, previous.GetProperty("occurrenceCount").GetInt32());
        Assert.Equal(5, previous.GetProperty("reviewedExpectationCount").GetInt32());
        Assert.Single(previous.GetProperty("evidence").EnumerateArray());

        var empty = await client.GetFromJsonAsync<JsonElement>(
            "/internal/pattern-review?range=custom&from=2026-08-01&to=2026-08-07");
        var emptyTrend = empty.GetProperty("labels").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "insufficient_evidence").GetProperty("trend");
        Assert.Equal("insufficient_evidence", emptyTrend.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, emptyTrend.GetProperty("direction").ValueKind);
        Assert.Equal(0, emptyTrend.GetProperty("current").GetProperty("reviewedExpectationCount").GetInt32());
    }

    [Fact]
    public async Task Blank_capture_creates_nothing_and_cross_owner_edit_is_hidden()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);

        using var blank = await Post(client, "   ", "blank-1");
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(0, await fixture.Count("journal.market_observations"));
        Assert.Equal(0, await fixture.Count("journal.observation_updates"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/internal/market-observations/today")).StatusCode);

        using var created = await Post(client, "private", "private-1");
        var updateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var other = fixture.Client(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new { content = "stolen" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/internal/market-observations/today")).StatusCode);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string content, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/quick-observations") { Content = JsonContent.Create(new { content }) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer postgres;
        private readonly NpgsqlDataSource dataSource;
        private readonly WebApplicationFactory<Program> factory;
        private readonly FixedTimeProvider clock;
        private int seedDay;

        private Fixture(PostgreSqlContainer postgres, NpgsqlDataSource dataSource, WebApplicationFactory<Program> factory, FixedTimeProvider clock)
        {
            this.postgres = postgres;
            this.dataSource = dataSource;
            this.factory = factory;
            this.clock = clock;
        }

        internal static async Task<Fixture> StartAsync(DateTimeOffset now, bool dailyCloseUnavailable = false)
        {
            var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync();
            await using var setup = new NpgsqlConnection(postgres.GetConnectionString());
            await setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            foreach (var file in new[] { "0001_initial_journal_performance.sql", "0013_journal_idempotency.sql", "0026_market_observations.sql", "0028_observation_enrichment.sql", "0029_expectations.sql", "0030_expectation_reviews.sql", "0031_action_decisions_trades.sql", "0032_watchlist.sql", "0033_pattern_review_discipline_principles.sql", "0037_incremental_record_changes.sql", "0042_observation_search_indexes.sql", "0044_confirmed_patterns.sql", "0045_confirmed_pattern_lifecycle.sql" })
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", file)), setup).ExecuteNonQueryAsync();

            var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
            var clock = new FixedTimeProvider(now);
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.RemoveAll<IHostedService>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(dataSource);
                services.AddSingleton<TimeProvider>(clock);
                services.AddHttpClient("market-data").ConfigurePrimaryHttpMessageHandler(() => new InstrumentDirectoryHandler(dailyCloseUnavailable));
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuth.Scheme;
                    options.DefaultChallengeScheme = TestAuth.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
            })).WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Internal:ServiceKey"] = "test-service-key" })));
            return new Fixture(postgres, dataSource, factory, clock);
        }

        internal HttpClient Client(Guid userId, string timezone = "UTC", string rollover = "00:00")
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
            client.DefaultRequestHeaders.Add("X-Test-Timezone", timezone);
            client.DefaultRequestHeaders.Add("X-Test-Rollover", rollover);
            return client;
        }

        internal HttpClient RawClient() => factory.CreateClient();

        internal HttpClient ServiceClient()
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Service-Key", "test-service-key");
            return client;
        }

        internal void SetNow(DateTimeOffset value) => clock.Set(value);

        internal async Task SetReviewCreatedAt(Guid expectationId, DateTime value)
        {
            await using var command = dataSource.CreateCommand("UPDATE journal.expectation_reviews SET created_at=$2 WHERE expectation_id=$1");
            command.Parameters.AddWithValue(expectationId);
            command.Parameters.AddWithValue(value);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task SetUpdateRecordedAt(Guid updateId, DateTime value)
        {
            await using var command = dataSource.CreateCommand("UPDATE journal.observation_updates SET recorded_at=$2 WHERE id=$1");
            command.Parameters.AddWithValue(updateId);
            command.Parameters.AddWithValue(value);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task SoftDeleteUpdate(Guid updateId)
        {
            await using var command = dataSource.CreateCommand("UPDATE journal.observation_updates SET deleted_at=now() WHERE id=$1");
            command.Parameters.AddWithValue(updateId);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task SeedReview(Guid userId, DateTime reviewedAt, bool includeLabel = true)
        {
            var observationId = Guid.NewGuid();
            var updateId = Guid.NewGuid();
            var expectationId = Guid.NewGuid();
            var reviewId = Guid.NewGuid();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using (var observation = new NpgsqlCommand("""
                INSERT INTO journal.market_observations(id,user_id,journal_day,timezone,rollover_time)
                VALUES($1,$2,$3,'UTC','00:00')
                """, connection, tx))
            {
                observation.Parameters.AddWithValue(observationId);
                observation.Parameters.AddWithValue(userId);
                observation.Parameters.AddWithValue(new DateOnly(2000, 1, 1).AddDays(Interlocked.Increment(ref seedDay)));
                await observation.ExecuteNonQueryAsync();
            }
            await using (var update = new NpgsqlCommand("""
                INSERT INTO journal.observation_updates(id,market_observation_id,user_id,content,recorded_at)
                VALUES($1,$2,$3,'Boundary evidence',$4)
                """, connection, tx))
            {
                update.Parameters.AddWithValue(updateId);
                update.Parameters.AddWithValue(observationId);
                update.Parameters.AddWithValue(userId);
                update.Parameters.AddWithValue(reviewedAt);
                await update.ExecuteNonQueryAsync();
            }
            await using (var expectation = new NpgsqlCommand("""
                INSERT INTO journal.expectations(id,observation_update_id,user_id,expected_behavior,deadline,invalidation_condition,confidence,market)
                VALUES($1,$2,$3,'Boundary expectation',$4,'Boundary invalidation','medium','US')
                """, connection, tx))
            {
                expectation.Parameters.AddWithValue(expectationId);
                expectation.Parameters.AddWithValue(updateId);
                expectation.Parameters.AddWithValue(userId);
                expectation.Parameters.AddWithValue(reviewedAt);
                await expectation.ExecuteNonQueryAsync();
            }
            await using (var review = new NpgsqlCommand("""
                INSERT INTO journal.expectation_reviews(id,expectation_id,user_id,outcome,reasoning_quality,created_at,updated_at)
                VALUES($1,$2,$3,'confirmed','sound',$4,$4)
                """, connection, tx))
            {
                review.Parameters.AddWithValue(reviewId);
                review.Parameters.AddWithValue(expectationId);
                review.Parameters.AddWithValue(userId);
                review.Parameters.AddWithValue(reviewedAt);
                await review.ExecuteNonQueryAsync();
            }
            if (includeLabel)
            {
                await using var label = new NpgsqlCommand("""
                    INSERT INTO journal.expectation_review_labels(id,review_id,user_id,kind,system_key)
                    VALUES($1,$2,$3,'issue','insufficient_evidence')
                    """, connection, tx);
                label.Parameters.AddWithValue(Guid.NewGuid());
                label.Parameters.AddWithValue(reviewId);
                label.Parameters.AddWithValue(userId);
                await label.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }

        internal async Task<long> Count(string table)
        {
            await using var command = dataSource.CreateCommand($"SELECT count(*) FROM {table}");
            return (long)(await command.ExecuteScalarAsync())!;
        }

        internal async Task<long> PatternStateCount(Guid patternId, string state)
        {
            await using var command = dataSource.CreateCommand(
                "SELECT count(*) FROM journal.confirmed_pattern_state_events WHERE confirmed_pattern_id=$1 AND state=$2");
            command.Parameters.AddWithValue(patternId);
            command.Parameters.AddWithValue(state);
            return (long)(await command.ExecuteScalarAsync())!;
        }

        internal async Task<long> PatternEvidenceCount(Guid patternId)
        {
            await using var command = dataSource.CreateCommand(
                "SELECT count(*) FROM journal.confirmed_pattern_evidence WHERE confirmed_pattern_id=$1");
            command.Parameters.AddWithValue(patternId);
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            factory.Dispose();
            await dataSource.DisposeAsync();
            await postgres.DisposeAsync();
        }
    }

    private sealed class InstrumentDirectoryHandler(bool dailyCloseUnavailable = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var dailyClose = request.RequestUri?.AbsolutePath.EndsWith("/daily-close", StringComparison.OrdinalIgnoreCase) is true
                && request.RequestUri.AbsolutePath.Contains(KnownInstrumentId.ToString(), StringComparison.OrdinalIgnoreCase);
            if (dailyClose && dailyCloseUnavailable) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (dailyClose) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    instrumentId = KnownInstrumentId, symbol = "AAPL", status = "available", tradingDate = "2026-07-14",
                    rawClose = 100m, adjustedClose = 50m, provider = "test", publishedAt = "2026-07-14T22:00:00Z",
                }),
            });
            var known = request.RequestUri?.AbsolutePath.EndsWith(KnownInstrumentId.ToString(), StringComparison.OrdinalIgnoreCase) is true;
            return Task.FromResult(known
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { instrumentId = KnownInstrumentId, symbol = "AAPL", name = "Apple Inc.", exchange = "NASDAQ", currency = "USD", timezone = "America/New_York" }) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset current = value;
        internal void Set(DateTimeOffset next) => current = next;
        public override DateTimeOffset GetUtcNow() => current;
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "quick-observation-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("sub", id),
                new Claim("account_type", "human"),
                new Claim("timezone", Request.Headers["X-Test-Timezone"].FirstOrDefault() ?? "UTC"),
                new Claim("journal_day_rollover", Request.Headers["X-Test-Rollover"].FirstOrDefault() ?? "00:00"),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
