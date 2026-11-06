using CekideApi.Domain;
using System.Text.Json.Serialization;

namespace CekideApi.Application.Kiosks;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateKioskSessionRequest(string? Label);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivateKioskRequest(string? ActivationCode);

public sealed record KioskSessionDto(
    int Id,
    string Label,
    string Status,
    DateTime CreatedAt,
    DateTime ActivationExpiresAt,
    DateTime? ActivatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt)
{
    public static KioskSessionDto From(KioskSession session, DateTimeOffset now)
    {
        var status = session.RevokedAt is not null
            ? "revoked"
            : session.IsActive(now)
                ? "active"
                : session.ActivatedAt is not null || session.ActivationExpiresAt <= now.UtcDateTime
                    ? "expired"
                    : "pending";

        return new KioskSessionDto(
            session.Id, session.Label, status, session.CreatedAt, session.ActivationExpiresAt,
            session.ActivatedAt, session.ExpiresAt, session.RevokedAt);
    }
}

public sealed record KioskActivationDto(
    KioskSessionDto Session,
    string ActivationCode,
    DateTime ActivationExpiresAt);

public sealed record KioskCredentialDto(int SessionId, string Token, DateTime ExpiresAt);
