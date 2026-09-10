namespace SwLicenseWatcher.Core;

public interface ILocalStateProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedPayload);
}
