using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Shellwright.Api.Email;

namespace Shellwright.Api.Tests.Infrastructure;

/// <summary>Boots the real application against the real database.</summary>
/// <remarks>
/// ⚠️ Nothing is substituted except the clock and the mail provider, and both
/// for the same reason: they are the two dependencies whose real behaviour is
/// "wait" and "reach the internet". Everything else — the middleware order, the
/// authentication schemes, the policies, the connection interceptor, PostgreSQL
/// itself — is what runs in production. A harness that swapped the database out
/// would test a different application from the one that ships.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class ApiFactory(PostgresFixture fixture) : WebApplicationFactory<Program>
{
    /// <summary>Messages the API tried to send, in order.</summary>
    public CapturedEmail Email { get; } = new();

    /// <summary>Controllable clock, so expiry and backoff are testable without sleeping.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));

    /// <summary>Where uploaded bytes land for this factory's lifetime.</summary>
    public string AssetDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "shellwright-assets", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        builder.UseSetting("Database:ConnectionString", fixture.AppConnectionString);
        builder.UseSetting("Auth:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Auth:Issuer", "https://api.test");
        builder.UseSetting("Auth:Audience", "shellwright-test");
        builder.UseSetting("Auth:StudioOrigin", "https://studio.test");
        builder.UseSetting("Email:From", "Shellwright <no-reply@test>");
        builder.UseSetting("Build:ShellVersion", "1.0.0");
        builder.UseSetting("Build:Toolchain:agp", "8.9");
        builder.UseSetting("Build:Toolchain:xcode", "26.1");
        builder.UseSetting("AssetStorage:Directory", AssetDirectory);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(AssetDirectory))
        {
            Directory.Delete(AssetDirectory, recursive: true);
        }
    }

    /// <summary>Creates a client that does not follow redirects, so they can be asserted on.</summary>
    /// <returns>A configured client.</returns>
    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Registers an account and returns its address and password.</summary>
    /// <param name="client">The client to use.</param>
    /// <returns>The credentials that were created.</returns>
    public static async Task<(string Email, string Password)> RegisterAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var email = $"user-{Guid.NewGuid():N}@example.test";
        const string password = "correct horse battery staple";

        var response = await client.PostAsJsonAsync("/v1/auth/register", new { email, password });
        response.EnsureSuccessStatusCode();

        return (email, password);
    }
}

/// <summary>Collects what the API tried to send instead of sending it.</summary>
public sealed class CapturedEmail : IEmailSender
{
    private readonly List<EmailMessage> messages = [];

    /// <summary>Everything sent so far, oldest first.</summary>
    public IReadOnlyList<EmailMessage> Messages
    {
        get
        {
            lock (messages)
            {
                return [.. messages];
            }
        }
    }

    /// <summary>The most recent message to an address.</summary>
    /// <param name="to">The recipient.</param>
    /// <returns>The message, or null if none was sent.</returns>
    public EmailMessage? Last(string to)
    {
        lock (messages)
        {
            return messages.FindLast(x => x.To == to);
        }
    }

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (messages)
        {
            messages.Add(message);
        }

        return Task.CompletedTask;
    }
}
