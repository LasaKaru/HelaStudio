using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Shellwright.Api.Email;

/// <summary>A message to one recipient.</summary>
/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="Body">Plain-text body.</param>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Sends transactional email.</summary>
public interface IEmailSender
{
    /// <summary>Sends one message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the provider has accepted it.</returns>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Email settings.</summary>
public sealed class EmailOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Email";

    /// <summary>Resend API key. Empty in development, where mail is logged instead.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>From address, which must be on a domain verified with the provider.</summary>
    [Required]
    public string From { get; set; } = "Shellwright <no-reply@localhost>";
}

/// <summary>
/// Writes the message to the log instead of sending it.
/// </summary>
/// <remarks>
/// ⚠️ Selected only when no API key is configured, and it says so at startup.
/// The failure mode this guards against is a production deployment that
/// silently posts verification links to a log file — so the absence of a key is
/// loud, and the log line is unmistakable.
/// </remarks>
/// <param name="logger">Where the message goes.</param>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogWarning(
            "Email not sent — no provider configured. To: {To}. Subject: {Subject}.\n{Body}",
            message.To,
            message.Subject,
            message.Body);

        return Task.CompletedTask;
    }
}

/// <summary>Sends through Resend's HTTP API.</summary>
/// <param name="client">Configured HTTP client.</param>
/// <param name="options">Email settings.</param>
public sealed class ResendEmailSender(HttpClient client, IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var response = await client.PostAsJsonAsync(
            "emails",
            new
            {
                from = settings.From,
                to = new[] { message.To },
                subject = message.Subject,
                text = message.Body,
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
