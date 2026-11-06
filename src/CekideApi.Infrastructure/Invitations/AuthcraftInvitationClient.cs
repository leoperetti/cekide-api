using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Infrastructure.Invitations;

/// <summary>Calls the invitation endpoints of authcraft-api with the access token of the invoking user.</summary>
public sealed class AuthcraftInvitationClient(HttpClient http, AuthcraftOptions options) : IAuthcraftInvitationClient
{
    private sealed record InvitationResponse(long? InvitationId, DateTimeOffset? ExpiresAt);
    private sealed record StudentInvitationRequest(string Email, StudentInvitationProfile StudentProfile);

    public Task<CreatedInvitation> CreateAsync(string email, string accessToken, CancellationToken cancellationToken = default) =>
        SendAsync("api/invitations", new { email }, accessToken, cancellationToken);

    public Task<CreatedInvitation> CreateAsync(
        string email,
        StudentInvitationProfile profile,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendAsync("api/invitations", new StudentInvitationRequest(email, profile), accessToken, cancellationToken);

    public Task<CreatedInvitation> ResendAsync(long invitationId, string accessToken, CancellationToken cancellationToken = default) =>
        SendAsync($"api/invitations/{invitationId}/resend", null, accessToken, cancellationToken);

    public Task<CreatedInvitation> ResendAsync(
        long invitationId,
        string email,
        StudentInvitationProfile profile,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            $"api/invitations/{invitationId}/resend",
            new StudentInvitationRequest(email, profile),
            accessToken,
            cancellationToken);

    public async Task<AcceptedStudentProfile?> GetAcceptedProfileAsync(
        long invitationId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var endpoint = CreateEndpoint($"api/invitations/{invitationId}/accepted-profile");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when ((exception is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamServiceException("authcraft-api could not be reached.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await MapErrorAsync(response, cancellationToken);
            }

            try
            {
                var accepted = await response.Content.ReadFromJsonAsync<AcceptedStudentProfile>(
                    JsonSerializerOptions.Web,
                    cancellationToken);
                return accepted is { InvitationId: > 0, StudentId: > 0 }
                       && accepted.InvitationId == invitationId
                       && !string.IsNullOrWhiteSpace(accepted.IdentityId)
                    ? accepted
                    : throw new UpstreamServiceException("authcraft-api returned an unexpected accepted profile.");
            }
            catch (JsonException)
            {
                throw new UpstreamServiceException("authcraft-api returned an unreadable accepted profile.");
            }
        }
    }

    private async Task<CreatedInvitation> SendAsync(string path, object? body, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CreateEndpoint(path))
        {
            Content = body is null ? null : JsonContent.Create(body, options: JsonSerializerOptions.Web)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when ((exception is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamServiceException("authcraft-api could not be reached.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await MapErrorAsync(response, cancellationToken);
            }

            var created = await ReadAsync(response, cancellationToken);
            return created is { InvitationId: { } id, ExpiresAt: { } expiresAt }
                ? new CreatedInvitation(id, expiresAt)
                : throw new UpstreamServiceException("authcraft-api returned an unexpected invitation response.");
        }
    }

    private Uri CreateEndpoint(string path)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl)
            || !Uri.TryCreate(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            throw new UpstreamServiceException("authcraft-api is not configured (Authcraft:BaseUrl).");
        }

        return new Uri(baseUri, path);
    }

    private static async Task<InvitationResponse?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<InvitationResponse>(JsonSerializerOptions.Web, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<Exception> MapErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = await ReadDetailAsync(response, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.Conflict => new ConflictException(detail ?? "There is already a pending invitation."),
            HttpStatusCode.BadRequest => new DomainValidationException(detail ?? "authcraft-api rejected the invitation."),
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
                new ForbiddenException("authcraft-api did not authorize this request."),
            HttpStatusCode.NotFound => new NotFoundException(detail ?? "The invitation was not found in authcraft-api."),
            _ => new UpstreamServiceException($"authcraft-api responded {(int)response.StatusCode}.")
        };
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
                ? detail.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
