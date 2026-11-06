namespace CekideApi.Application.Abstractions;

public sealed record EmailMessage(string ToEmail, string ToName, string Subject, string HtmlBody);

public sealed record EmailSendResult(bool Succeeded, string? Error = null, bool DeliveryUncertain = false)
{
    public static EmailSendResult Ok() => new(true);

    public static EmailSendResult Fail(string error) => new(false, error);

    public static EmailSendResult Uncertain(string error) => new(false, error, DeliveryUncertain: true);
}

/// <summary>Sends transactional email. Delivery problems are reported in the result, not thrown.</summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
