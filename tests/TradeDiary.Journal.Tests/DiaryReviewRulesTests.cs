public sealed class DiaryReviewRulesTests
{
    [Fact]
    public void Review_validation_accepts_optional_fields_and_known_values()
    {
        var review = new DiaryReviewWrite(null, null, null, "calm", 5, null, "good", ["no_plan", "fomo"], null, null);

        Assert.Null(DiaryReviewRules.Validate(review));
    }

    [Theory]
    [InlineData(0, null, "invalid_discipline_score")]
    [InlineData(6, null, "invalid_discipline_score")]
    [InlineData(null, 0, "invalid_execution_score")]
    [InlineData(null, 6, "invalid_execution_score")]
    public void Review_validation_rejects_scores_outside_one_to_five(int? discipline, int? execution, string error) =>
        Assert.Equal(error, DiaryReviewRules.Validate(new(null, null, null, null, (short?)discipline, (short?)execution, null, [], null, null)));

    [Fact]
    public void Review_validation_rejects_unknown_enums_and_tags()
    {
        Assert.Equal("invalid_emotion", DiaryReviewRules.Validate(new(null, null, null, "excited", null, null, null, [], null, null)));
        Assert.Equal("invalid_process_assessment", DiaryReviewRules.Validate(new(null, null, null, null, null, null, "excellent", [], null, null)));
        Assert.Equal("invalid_mistake_tag", DiaryReviewRules.Validate(new(null, null, null, null, null, null, null, ["invented"], null, null)));
        Assert.Equal("duplicate_mistake_tag", DiaryReviewRules.Validate(new(null, null, null, null, null, null, null, ["fomo", "fomo"], null, null)));
    }

    [Theory]
    [InlineData("2025-01-01", "2026-01-02", true)]
    [InlineData("2025-01-01", "2026-01-01", false)]
    [InlineData("2026-01-02", "2026-01-01", true)]
    public void Summary_range_is_limited_to_366_days(string from, string to, bool invalid) =>
        Assert.Equal(invalid, DiaryReviewRules.InvalidRange(DateOnly.Parse(from), DateOnly.Parse(to)));
}
