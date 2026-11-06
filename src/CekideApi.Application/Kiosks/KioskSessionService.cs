using System.Security.Cryptography;
using System.Text;
using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Kiosks;

public sealed class KioskSessionService(
    IKioskSessionRepository sessions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<KioskActivationDto> CreateAsync(
        string? label,
        string tenant,
        string? actor,
        CancellationToken cancellationToken = default)
    {
        var activationCode = CreateOpaqueToken(16);
        var now = clock.UtcNow;
        var session = KioskSession.Create(label, tenant, actor, Hash(activationCode), now);
        sessions.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new KioskActivationDto(
            KioskSessionDto.From(session, now),
            activationCode,
            session.ActivationExpiresAt);
    }

    public async Task<IReadOnlyList<KioskSessionDto>> ListAsync(
        string tenant,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var rows = await sessions.ListForTenantAsync(tenant, cancellationToken);
        return rows.Select(session => KioskSessionDto.From(session, now)).ToArray();
    }

    public async Task RevokeAsync(
        int id,
        string tenant,
        string? actor,
        CancellationToken cancellationToken = default)
    {
        var session = await sessions.GetByIdAsync(id, cancellationToken);
        if (session is null || !string.Equals(session.Tenant, tenant, StringComparison.Ordinal))
        {
            throw new NotFoundException("Kiosk session not found.");
        }

        if (session.RevokedAt is not null)
        {
            return;
        }

        session.Revoke(actor, clock.UtcNow);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            if (!await sessions.IsRevokedAsync(id, tenant, cancellationToken))
            {
                throw;
            }
        }
    }

    public async Task<KioskCredentialDto> ActivateAsync(
        string? activationCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(activationCode))
        {
            throw InvalidActivationCode();
        }

        var normalizedCode = activationCode.Trim();
        var codeHash = Hash(normalizedCode);
        var session = await sessions.GetByActivationCodeHashAsync(codeHash, cancellationToken);
        if (session is null)
        {
            throw InvalidActivationCode();
        }

        var token = CreateOpaqueToken(32);
        var now = clock.UtcNow;
        try
        {
            session.Activate(codeHash, Hash(token), now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            throw InvalidActivationCode();
        }

        return new KioskCredentialDto(session.Id, token, session.ExpiresAt!.Value);
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string CreateOpaqueToken(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static AttendanceException InvalidActivationCode() =>
        new(400, "invalid_activation_code", "Activation code is invalid, expired, or already used.");
}
