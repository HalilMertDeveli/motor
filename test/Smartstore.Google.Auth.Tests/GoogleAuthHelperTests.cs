using System;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using NUnit.Framework;
using Smartstore.Core.Identity;

namespace Smartstore.Google.Auth.Tests;

[TestFixture]
public class GoogleAuthHelperTests
{
    [TestCase("{\"sub\":\"1\",\"email\":\"a@example.com\",\"email_verified\":true}", true)]
    [TestCase("{\"sub\":\"1\",\"email\":\"a@example.com\",\"email_verified\":\"true\"}", true)]
    [TestCase("{\"sub\":\"1\",\"email\":\"a@example.com\",\"email_verified\":false}", false)]
    [TestCase("{\"sub\":\"1\",\"email\":\"a@example.com\",\"email_verified\":\"false\"}", false)]
    [TestCase("{\"sub\":\"1\",\"email\":\"a@example.com\"}", false)]
    [TestCase("[]", false)]
    public void IsEmailVerified_requires_an_explicit_true(string json, bool expected)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.That(GoogleAuthHelper.IsEmailVerified(doc.RootElement), Is.EqualTo(expected));
    }

    [Test]
    public void GetErrorCode_maps_unverified_email()
    {
        Assert.That(GoogleAuthHelper.GetErrorCode(new GoogleEmailNotVerifiedException()),
            Is.EqualTo(ExternalAuthenticationErrorCodes.EmailNotVerified));

        Assert.That(GoogleAuthHelper.GetErrorCode(new AuthenticationFailureException("An error was encountered while handling the remote login.", new GoogleEmailNotVerifiedException())),
            Is.EqualTo(ExternalAuthenticationErrorCodes.EmailNotVerified));
    }

    [TestCase("Correlation failed.")]
    [TestCase("The oauth state was missing or invalid.")]
    public void GetErrorCode_maps_invalid_state_and_correlation_to_session_expired(string message)
    {
        Assert.That(GoogleAuthHelper.GetErrorCode(new AuthenticationFailureException(message)),
            Is.EqualTo(ExternalAuthenticationErrorCodes.SessionExpired));
    }

    [Test]
    public void GetErrorCode_returns_null_for_provider_and_configuration_errors()
    {
        Assert.That(GoogleAuthHelper.GetErrorCode(new AuthenticationFailureException("OAuth token endpoint failure: Status: BadRequest;Body: {\"error\":\"invalid_client\"};")), Is.Null);
        Assert.That(GoogleAuthHelper.GetErrorCode(new InvalidOperationException("Something else")), Is.Null);
        Assert.That(GoogleAuthHelper.GetErrorCode(null), Is.Null);
    }

    [Test]
    public void BuildErrorUrl_is_local_and_carries_only_the_error_code()
    {
        Assert.That(GoogleAuthHelper.BuildErrorUrl(string.Empty, ExternalAuthenticationErrorCodes.AccessDenied),
            Is.EqualTo("/identity/externalerrorcallback?provider=google&errorCode=access_denied"));

        Assert.That(GoogleAuthHelper.BuildErrorUrl("/shop/", null),
            Is.EqualTo("/shop/identity/externalerrorcallback?provider=google"));

        Assert.That(GoogleAuthHelper.BuildErrorUrl(null, "a&b=c"),
            Is.EqualTo("/identity/externalerrorcallback?provider=google&errorCode=a%26b%3Dc"));
    }
}
