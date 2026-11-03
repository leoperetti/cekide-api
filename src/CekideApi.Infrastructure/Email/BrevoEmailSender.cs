using System.Net.Http.Json;
using CekideApi.Application.Abstractions;

namespace CekideApi.Infrastructure.Email;

/// <summary>Sends transactional email through the Brevo REST API (POST /smtp/email).</summary>
public sealed class BrevoEmailSender(HttpClient http, BrevoOptions options) : IEmailSender
{
    private const int MaxErrorLength = 500;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.FromEmail))
        {
            return EmailSendResult.Fail("Brevo is not configured (Brevo:ApiKey and Brevo:FromEmail are required).");
        }

        var payload = new
        {
            sender = new { email = options.FromEmail, name = options.FromName },
            to = new[] { new { email = message.ToEmail, name = message.ToName } },
            subject = message.Subject,
            htmlContent = message.HtmlBody
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl), "smtp/email"))
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("api-key", options.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return EmailSendResult.Ok();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return EmailSendResult.Fail(Truncate($"Brevo responded {(int)response.StatusCode}: {body}"));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return EmailSendResult.Fail(Truncate($"Brevo request failed: {exception.Message}"));
        }
    }

    private static string Truncate(string value) => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
