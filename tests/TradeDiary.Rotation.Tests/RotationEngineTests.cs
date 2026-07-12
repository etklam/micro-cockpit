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
}
