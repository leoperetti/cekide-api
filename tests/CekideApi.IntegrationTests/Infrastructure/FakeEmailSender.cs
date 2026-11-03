using CekideApi.Application.Abstractions;

namespace CekideApi.IntegrationTests.Infrastructure;

public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    /// <summary>When set, every send fails with this error.</summary>
    public string? FailWith { get; set; }

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            return Task.FromResult(EmailSendResult.Fail(FailWith));
        }

        Sent.Add(message);
        return Task.FromResult(EmailSendResult.Ok());
    }
}
