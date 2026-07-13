public sealed class DiaryDeletedHandlerTests
{
    [Fact]
    public void IsValid_accepts_the_supported_typed_event()
    {
        var input = new DiaryDeletedV1Envelope(
            Guid.NewGuid(),
            DiaryDeletedHandler.EventType,
            DiaryDeletedHandler.EventVersion,
            new DiaryDeletedV1(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(DiaryDeletedHandler.IsValid(input));
    }

    [Fact]
    public void IsValid_rejects_wrong_version_and_empty_ids()
    {
        var input = new DiaryDeletedV1Envelope(
            Guid.NewGuid(),
            DiaryDeletedHandler.EventType,
            DiaryDeletedHandler.EventVersion + 1,
            new DiaryDeletedV1(Guid.Empty, Guid.NewGuid()));

        Assert.False(DiaryDeletedHandler.IsValid(input));
    }
}
