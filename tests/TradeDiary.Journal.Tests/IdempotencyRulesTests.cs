public sealed class IdempotencyRulesTests
{
    [Fact]
    public void Normalize_reuses_a_trimmed_key_and_rejects_oversized_keys()
    {
        Assert.Equal("same-key", IdempotencyRules.Normalize("  same-key "));
        Assert.Null(IdempotencyRules.Normalize("   "));
        Assert.True(IdempotencyRules.IsValid(new string('x', 200)));
        Assert.False(IdempotencyRules.IsValid(new string('x', 201)));
    }

    [Fact]
    public void Request_hash_is_deterministic_and_changes_with_the_body()
    {
        Assert.Equal(IdempotencyRules.ComputeRequestHash(new { content = "one" }), IdempotencyRules.ComputeRequestHash(new { content = "one" }));
        Assert.NotEqual(IdempotencyRules.ComputeRequestHash(new { content = "one" }), IdempotencyRules.ComputeRequestHash(new { content = "two" }));
    }
}
