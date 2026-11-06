using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.IntegrationTests.Infrastructure;

/// <summary>Stands in for authcraft-api: records what cekide-api delegates and can fail on demand.</summary>
public sealed class FakeAuthcraftInvitationClient : IAuthcraftInvitationClient
{
    public sealed record Call(
        string Operation, string? Email, long? InvitationId, string AccessToken, StudentInvitationProfile? StudentProfile = null);

    private long _nextInvitationId = 100;

    public List<Call> Calls { get; } = [];

    /// <summary>Thrown (once) by the next call, to emulate authcraft-api answering 409, 404, 502...</summary>
    public Exception? FailNext { get; set; }

    public AcceptedStudentProfile? AcceptedProfile { get; set; }

    public Task<CreatedInvitation> CreateAsync(string email, string accessToken, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call("create", email, null, accessToken));
        return Respond();
    }

    public Task<CreatedInvitation> CreateAsync(
        string email, StudentInvitationProfile profile, string accessToken, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call("create", email, null, accessToken, profile));
        return Respond();
    }

    public Task<CreatedInvitation> ResendAsync(long invitationId, string accessToken, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call("resend", null, invitationId, accessToken));
        return Respond();
    }

    public Task<CreatedInvitation> ResendAsync(
        long invitationId, string email, StudentInvitationProfile profile, string accessToken, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call("resend", email, invitationId, accessToken, profile));
        return Respond();
    }

    public Task<AcceptedStudentProfile?> GetAcceptedProfileAsync(
        long invitationId, string accessToken, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call("accepted-profile", null, invitationId, accessToken));
        if (FailNext is { } failure)
        {
            FailNext = null;
            throw failure;
        }

        return Task.FromResult(AcceptedProfile);
    }

    private Task<CreatedInvitation> Respond()
    {
        if (FailNext is { } failure)
        {
            FailNext = null;
            throw failure;
        }

        var id = _nextInvitationId++;
        return Task.FromResult(new CreatedInvitation(id, new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero).AddMinutes(id)));
    }
}
