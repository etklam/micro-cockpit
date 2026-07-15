public sealed class AuthorizationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("wrong", false)]
    [InlineData("TEST-ONLY-NOT-A-SECRET", true)]
    public void Audit_write_service_key_is_required_and_exact(string? supplied, bool expected) =>
        Assert.Equal(expected, ServiceKeyAuthorization.IsValid(supplied, "TEST-ONLY-NOT-A-SECRET"));

    [Theory]
    [InlineData("Bearer human-jwt")]
    [InlineData("Bearer agent-jwt")]
    public void Bearer_principals_cannot_authorize_audit_writes(string bearer) =>
        Assert.False(ServiceKeyAuthorization.IsValid(bearer, "TEST-ONLY-NOT-A-SECRET"));
}
