public sealed class RotationEngineTests
{
    [Fact]
    public void Uses_the_formula_version_as_a_stable_idempotency_dimension()
    {
        Assert.Equal("rotation-v1", RotationEngine.FormulaVersion);
    }

    [Fact]
    public void Marks_only_full_history_as_sufficient()
    {
        Assert.True(RotationEngine.HasSufficientData(200));
        Assert.False(RotationEngine.HasSufficientData(199));
    }

    [Fact]
    public void Retries_insufficient_data_when_the_published_source_advances()
    {
        var previous = new DateOnly(2025, 7, 18);
        var current = new DateOnly(2025, 7, 19);

        Assert.True(RotationEngine.ShouldRetryInsufficientData(previous, current));
        Assert.True(RotationEngine.ShouldRetryInsufficientData(null, current));
        Assert.False(RotationEngine.ShouldRetryInsufficientData(previous, previous));
        Assert.False(RotationEngine.ShouldRetryInsufficientData(current, previous));
        Assert.False(RotationEngine.ShouldRetryInsufficientData(previous, null));
    }

    [Fact]
    public void Reuses_completed_batches_and_only_retries_stable_insufficient_data()
    {
        var previous = new DateOnly(2025, 7, 18);
        var current = new DateOnly(2025, 7, 19);

        Assert.True(RotationEngine.ShouldReuseBatch("completed", previous, current));
        Assert.True(RotationEngine.ShouldReuseBatch("running", previous, current));
        Assert.True(RotationEngine.ShouldReuseBatch("insufficient_data", previous, previous));
        Assert.False(RotationEngine.ShouldReuseBatch("insufficient_data", previous, current));
        Assert.False(RotationEngine.ShouldReuseBatch("failed", previous, current));
    }
}
