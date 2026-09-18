namespace GoatClient.Services.Auth;

/// <summary>Secret storage for refresh tokens. Implemented with the Windows Credential Manager.</summary>
public interface ISecureTokenStore
{
    string? Read(string key);

    void Write(string key, string secret);

    void Delete(string key);
}
