public sealed class AuthorizationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("wrong", false)]
    [InlineData("local-service-key", true)]
    public void Service_key_is_required_and_exact(string? supplied, bool expected) =>
        Assert.Equal(expected, ServiceKeyAuthorization.IsValid(supplied, "local-service-key"));

    [Fact]
    public void Normal_bearer_value_is_not_a_service_key() =>
        Assert.False(ServiceKeyAuthorization.IsValid("Bearer valid-user-jwt", "local-service-key"));
}
