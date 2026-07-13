public sealed class DiaryDeletedEventTests
{
    [Fact]
    public void Create_builds_the_typed_v1_envelope()
    {
        var eventId = Guid.NewGuid();
        var diaryId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = DiaryDeletedV1Envelope.Create(eventId, diaryId, userId);

        Assert.Equal(eventId, result.EventId);
        Assert.Equal("DiaryDeleted.v1", result.EventType);
        Assert.Equal(1, result.Version);
        Assert.Equal(new DiaryDeletedV1(diaryId, userId), result.Payload);
    }

    [Fact]
    public void Create_rejects_an_empty_diary_id()
    {
        Assert.Throws<ArgumentException>(() => DiaryDeletedV1Envelope.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void Create_rejects_an_empty_user_id()
    {
        Assert.Throws<ArgumentException>(() => DiaryDeletedV1Envelope.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
    }
}
