namespace CekideApi.Infrastructure.Email;

/// <summary>Brevo settings; the API key comes from user-secrets or Key Vault, never from source control.</summary>
public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public string? ApiKey { get; set; }

    /// <summary>Verified sender address in Brevo.</summary>
    public string? FromEmail { get; set; }

    public string FromName { get; set; } = "Cekide Gym";

    public string BaseUrl { get; set; } = "https://api.brevo.com/v3/";
}
