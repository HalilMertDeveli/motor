using FluentValidation;
using NUnit.Framework;

namespace Smartstore.Web.Tests.Common.Validation;

/// <summary>
/// MOTOR: the phone number rule must accept every real Turkish notation and only reject obvious garbage.
/// </summary>
[TestFixture]
public class PhoneNumberValidationTests
{
    [TestCase("+90 532 123 45 67")]
    [TestCase("+905321234567")]
    [TestCase("0532 123 45 67")]
    [TestCase("05321234567")]
    [TestCase("5321234567")]
    [TestCase("(0212) 345 67 89")]
    [TestCase("0212-345-67-89")]
    [TestCase("0 (532) 123 45 67")]
    [TestCase("+90 (212) 345.67.89")]
    [TestCase("+49 30 1234567")]
    [TestCase("  0532 123 45 67  ")]
    [TestCase("")]
    [TestCase(null)]
    public void Accepts_Valid_Or_Empty_Numbers(string value)
    {
        Assert.That(FluentValidatorRuleExtensions.IsPlausiblePhoneNumber(value), Is.True);
    }

    [TestCase("abc")]
    [TestCase("0532 ABC 45 67")]
    [TestCase("12345")]
    [TestCase("90+5321234567")]
    [TestCase("+90 532 123 45 67 89 01 23")]
    [TestCase("0532;1234567")]
    public void Rejects_Implausible_Numbers(string value)
    {
        Assert.That(FluentValidatorRuleExtensions.IsPlausiblePhoneNumber(value), Is.False);
    }
}
