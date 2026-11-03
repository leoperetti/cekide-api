using CekideApi.Application.Abstractions;

namespace CekideApi.IntegrationTests.Infrastructure;

/// <summary>Stands in for authcraft-api: records what cekide-api delegates and can fail on demand.</summary>
public sealed class FakeAuthcraftInvitationClient : IAuthcraftInvitationClient
{
    public sealed record Call(string Operation, string? Email, long? InvitationId, string AccessToken);

    private long _nextInvitationId = 100;

    public List<Call> Calls { get; } = [];

    /// <summary>Thrown (once) by the next call, to emulate authcraft-api answering 409, 404, 502...</summary>
    public Exception? FailNext { get; set; }

    public Task<CreatedInvitation> CreateAsync(string email, string accessToken, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call("create", email, null, accessToken));
        return Respond();
    }

    public Task<CreatedInvitation> ResendAsync(long invitationId, string accessToken, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call("resend", null, invitationId, accessToken));
        return Respond();
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
