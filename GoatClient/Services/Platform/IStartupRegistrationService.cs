namespace GoatClient.Services.Platform;

/// <summary>Controls the "Launch launcher with Windows" option (HKCU Run key, no admin rights).</summary>
public interface IStartupRegistrationService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
