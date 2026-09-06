using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class UninstallGrantTests
{
    [Fact]
    public void CreateCode_is_eight_unambiguous_characters()
    {
        var code = UninstallGrant.CreateCode();
        Assert.Equal(UninstallGrant.CodeLength, code.Length);
        Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$", code);
    }

    [Fact]
    public void HashCode_is_stable_and_case_insensitive()
    {
        var hash = UninstallGrant.HashCode("ab12cd34");
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, UninstallGrant.HashCode("AB12CD34"));
        Assert.True(UninstallGrant.CodeMatchesHash(" ab12cd34 ", hash));
        Assert.False(UninstallGrant.CodeMatchesHash("ZZZZZZZZ", hash));
    }

    [Fact]
    public void CodeMatchesHash_rejects_invalid_hashes()
    {
        Assert.False(UninstallGrant.CodeMatchesHash("ABCDEFGH", "not-hex"));
        Assert.False(UninstallGrant.CodeMatchesHash("ABCDEFGH", null));
        Assert.False(UninstallGrant.CodeMatchesHash(null, UninstallGrant.HashCode("ABCDEFGH")));
    }

    [Fact]
    public void ResolveStatus_expires_approved_grants_after_the_deadline()
    {
        var expires = new DateTimeOffset(2026, 9, 6, 10, 15, 0, TimeSpan.Zero);
        Assert.Equal(
            UninstallGrant.Expired,
            UninstallGrant.ResolveStatus(UninstallGrant.Approved, expires, expires));
        Assert.Equal(
            UninstallGrant.Approved,
            UninstallGrant.ResolveStatus(UninstallGrant.Approved, expires, expires.AddSeconds(-1)));
        Assert.Equal(
            UninstallGrant.Pending,
            UninstallGrant.ResolveStatus(UninstallGrant.Pending, expiresAtUtc: null, expires));
        Assert.Equal(
            UninstallGrant.Denied,
            UninstallGrant.ResolveStatus(UninstallGrant.Denied, expires, expires.AddHours(1)));
    }
}
