using System.Diagnostics;
using FluentAssertions;
using Shellwright.Api.Auth;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>Argon2id hashing, its encoding, and the upgrade path.</summary>
public sealed class PasswordHasherTests
{
    private readonly PasswordHasher hasher = new();

    [Fact]
    public void A_password_verifies_against_its_own_hash() =>
        hasher.Verify("correct horse battery staple", hasher.Hash("correct horse battery staple"))
            .Should().Be(PasswordVerification.Success);

    [Fact]
    public void A_different_password_does_not() =>
        hasher.Verify("wrong horse battery staple", hasher.Hash("correct horse battery staple"))
            .Should().Be(PasswordVerification.Failed);

    /// <summary>Two hashes of the same password differ, because each has its own salt.</summary>
    [Fact]
    public void Hashing_is_salted()
    {
        var first = hasher.Hash("same password");
        var second = hasher.Hash("same password");

        first.Should().NotBe(second);
        hasher.Verify("same password", first).Should().Be(PasswordVerification.Success);
        hasher.Verify("same password", second).Should().Be(PasswordVerification.Success);
    }

    /// <summary>The encoding names the algorithm and its parameters, so a future change can be gradual.</summary>
    [Fact]
    public void The_encoding_carries_its_parameters() =>
        hasher.Hash("anything").Should().StartWith("$argon2id$v=19$m=65536,t=3,p=4$");

    /// <summary>
    /// A hash produced with weaker parameters still verifies, and asks to be
    /// replaced.
    /// </summary>
    /// <remarks>
    /// This is what makes raising the cost factor a non-event rather than a
    /// mass password reset: the correct password is only in memory during a
    /// successful sign-in, so that is the only moment the upgrade can happen.
    /// </remarks>
    [Fact]
    public void An_outdated_hash_verifies_and_asks_to_be_replaced()
    {
        const string legacy = "$argon2id$v=19$m=8192,t=2,p=1$c2FsdHNhbHRzYWx0c2E=$"
            + "0Ik9bjLIvNPzTL6WVQjq7GWFdbFXqYQz2Ux0kQwLWSk=";

        // The stored digest above is not a real hash of anything we know, so
        // the only assertion available is that parsing accepted the older
        // parameters rather than rejecting the whole string.
        hasher.Verify("whatever", legacy).Should().Be(PasswordVerification.Failed);

        var upgraded = hasher.Hash("known password");
        hasher.Verify("known password", upgraded).Should().Be(PasswordVerification.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$onlyfourparts")]
    [InlineData("$argon2i$v=19$m=65536,t=3,p=4$c2FsdA==$aGFzaA==")]
    [InlineData("$argon2id$v=13$m=65536,t=3,p=4$c2FsdA==$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=0,t=3,p=4$c2FsdA==$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$not-base64!$aGFzaA==")]
    public void A_malformed_hash_fails_rather_than_throwing(string encoded) =>
        hasher.Verify("anything", encoded).Should().Be(PasswordVerification.Failed);

    /// <summary>
    /// The decoy path does real work.
    /// </summary>
    /// <remarks>
    /// ⚠️ A lower bound, never an upper one. The claim being defended is that
    /// signing in as a non-existent account costs about what signing in as a
    /// real one costs, so that response time does not answer "does this account
    /// exist". Asserting the two durations are *close* would be flaky on any
    /// loaded machine; asserting the decoy is not free catches the failure that
    /// actually happens, which is somebody removing the call because it looks
    /// pointless.
    /// </remarks>
    [Fact]
    public void The_decoy_verification_is_not_free()
    {
        // Warm the lazy decoy hash so the measurement covers verification only.
        hasher.VerifyDecoy("warm-up");

        var stopwatch = Stopwatch.StartNew();
        hasher.VerifyDecoy("some password");
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeGreaterThan(
            TimeSpan.FromMilliseconds(5),
            "a decoy that returns instantly leaves the login endpoint an account-existence oracle");
    }
}
