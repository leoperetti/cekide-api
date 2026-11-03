namespace CekideApi.Infrastructure.Invitations;

public sealed class AuthcraftOptions
{
    public const string SectionName = "Authcraft";

    /// <summary>Base URL of authcraft-api, for example https://authcraft-api.example.com.</summary>
    public string? BaseUrl { get; set; }
}
