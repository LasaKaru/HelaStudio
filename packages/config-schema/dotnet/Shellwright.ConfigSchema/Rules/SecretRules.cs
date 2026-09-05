using System.Text.RegularExpressions;
using static Shellwright.ConfigSchema.Rules.JsonHelpers;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>
/// Detects credentials pasted into the configuration.
/// </summary>
/// <remarks>
/// Config is stored, hashed, logged, exported, and embedded in the shipped app
/// binary, where anyone can read it. A secret here is a secret published, so this
/// is an error rather than a warning.
/// </remarks>
public sealed partial class NoSecretsRule : IValidationRule
{
    private static readonly (string Name, Regex Test)[] Patterns =
    [
        ("an AWS access key id", AwsKey()),
        ("a GitHub token", GitHubToken()),
        ("a Slack token", SlackToken()),
        ("a Stripe secret key", StripeKey()),
        ("a Google API key", GoogleKey()),
        ("a private key block", PrivateKeyBlock()),
        ("a JSON web token", JsonWebToken()),
    ];

    /// <inheritdoc/>
    public string Name => "no-secrets";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var found = new List<Diagnostic>();
        WalkStrings(context.Config, [], (path, key, value) =>
        {
            foreach (var (name, test) in Patterns)
            {
                if (test.IsMatch(value))
                {
                    found.Add(Build(path, $"This looks like {name}."));
                    return;
                }
            }

            // A header literally named Authorization is a credential regardless of shape.
            if (SuspiciousKey().IsMatch(key) && value.Length >= 8)
            {
                found.Add(Build(path, $"A value under \"{key}\" is almost always a credential."));
            }
        });

        return found;
    }

    private static Diagnostic Build(string path, string why) => Diagnostic.Create(
        DiagnosticCode.SecretInConfig,
        Severity.Error,
        path,
        $"{why} App configuration is stored, exported, and embedded in the app itself, where anyone who " +
        "downloads it can read this value. Store the credential in your workspace credentials instead " +
        "and reference it by id, or have your website supply it after the user signs in.");

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsKey();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bxox[baprs]-[A-Za-z0-9-]{10,}\b")]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"\bsk_(?:live|test)_[A-Za-z0-9]{16,}\b")]
    private static partial Regex StripeKey();

    [GeneratedRegex(@"\bAIza[0-9A-Za-z_-]{35}\b")]
    private static partial Regex GoogleKey();

    [GeneratedRegex(@"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b")]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(
        @"^(?:authorization|x-api-key|api[-_]?key|secret|password|token|bearer)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SuspiciousKey();
}
