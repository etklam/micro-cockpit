using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

public static class SigningKeyIdentity
{
    public static string GetKeyId(RSA key) => Base64UrlEncoder.Encode(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
}
