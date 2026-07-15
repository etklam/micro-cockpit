public sealed class AuthorizationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("wrong", false)]
    [InlineData("TEST-ONLY-NOT-A-SECRET", true)]
    public void Service_key_is_required_and_exact(string? supplied, bool expected) =>
        Assert.Equal(expected, ServiceKeyAuthorization.IsValid(supplied, "TEST-ONLY-NOT-A-SECRET"));

    [Fact]
    public void Normal_bearer_value_is_not_a_service_key() =>
        Assert.False(ServiceKeyAuthorization.IsValid("Bearer valid-user-jwt", "TEST-ONLY-NOT-A-SECRET"));
}
