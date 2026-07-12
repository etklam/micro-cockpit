using System.Security.Cryptography;

public sealed class SigningKeyIdentityTests
{
    [Fact]
    public void KeyId_is_stable_when_the_same_rsa_key_is_reloaded()
    {
        using var original = RSA.Create(2048);
        var parameters = original.ExportParameters(true);
        using var restarted = RSA.Create();
        restarted.ImportParameters(parameters);

        Assert.Equal(SigningKeyIdentity.GetKeyId(original), SigningKeyIdentity.GetKeyId(restarted));
    }

    [Fact]
    public void KeyId_changes_when_the_signing_key_changes()
    {
        using var first = RSA.Create(2048);
        using var second = RSA.Create(2048);

        Assert.NotEqual(SigningKeyIdentity.GetKeyId(first), SigningKeyIdentity.GetKeyId(second));
    }
}
