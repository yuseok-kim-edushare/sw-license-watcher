using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public static class BearerTokenAuthenticator
{
    public static bool IsAuthorized(string? suppliedAuthorizationHeader, string expectedToken)
    {
        var supplied = suppliedAuthorizationHeader ?? string.Empty;
        var expected = string.Concat("Bearer ", expectedToken);
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }

    public static bool IsAuthorized(
        string? suppliedAuthorizationHeader,
        ApiSecurityOptions security,
        PathString requestPath) =>
        IsAuthorized(suppliedAuthorizationHeader, security, requestPath, HttpMethods.Get);

    public static bool IsAuthorized(
        string? suppliedAuthorizationHeader,
        ApiSecurityOptions security,
        PathString requestPath,
        string httpMethod)
    {
        ArgumentNullException.ThrowIfNull(security);

        var supplied = suppliedAuthorizationHeader ?? string.Empty;
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var matchesAgent = MatchesConfiguredToken(suppliedHash, security.AgentToken);
        var matchesAdmin = MatchesConfiguredToken(suppliedHash, security.AdminToken);
        var agentEndpoint = EndpointPolicies.IsAgentEndpoint(requestPath, httpMethod);
        var adminEndpoint = EndpointPolicies.IsAdminEndpoint(requestPath, httpMethod);
        return (matchesAgent & agentEndpoint) | (matchesAdmin & adminEndpoint);
    }

    private static bool MatchesConfiguredToken(byte[] suppliedHash, string expectedToken)
    {
        var expected = string.Concat("Bearer ", expectedToken);
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var equal = CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
        return equal & ApiSecurityOptionsValidator.HasUsableToken(expectedToken);
    }
}
