using System;
using Microsoft.OData;
using NUnit.Framework;
using Smartstore.Web.Api;

namespace Smartstore.Web.Tests.Common.Api;

[TestFixture]
public class ODataHelperTests
{
    const string SecretMessage = "Cannot open database \"MotorShop\" requested by the login.";

    [Test]
    public void UnhandledError_Hides_Message_And_Stack_Trace_When_Details_Disabled()
    {
        var error = ODataHelper.CreateUnhandledError(Throw(new InvalidOperationException(SecretMessage)), 500, showDetails: false);
        var json = error.ToString();

        Assert.That(error.Message, Is.EqualTo(ODataHelper.UnknownErrorMessage));
        Assert.That(error.Code, Is.EqualTo("500"));
        Assert.That(error.InnerError, Is.Null);
        Assert.That(json, Does.Not.Contain("MotorShop"));
        Assert.That(json, Does.Not.Contain(nameof(InvalidOperationException)));
        Assert.That(json, Does.Not.Contain(nameof(ODataHelperTests)));
    }

    [Test]
    public void UnhandledError_Shows_Message_And_Stack_Trace_When_Details_Enabled()
    {
        var error = ODataHelper.CreateUnhandledError(Throw(new InvalidOperationException(SecretMessage)), 500, showDetails: true);
        var json = error.ToString();

        Assert.That(error.Message, Is.EqualTo(SecretMessage));
        Assert.That(error.InnerError, Is.Not.Null);
        Assert.That(json, Does.Contain(typeof(InvalidOperationException).FullName));
        Assert.That(json, Does.Contain(nameof(ODataHelperTests)));
    }

    [Test]
    public void UnhandledError_Keeps_OData_Message_Without_Stack_Trace_When_Details_Disabled()
    {
        const string message = "The query specified in the URI is not valid.";

        var error = ODataHelper.CreateUnhandledError(Throw(new ODataException(message)), 500, showDetails: false);

        Assert.That(error.Message, Is.EqualTo(message));
        Assert.That(error.InnerError, Is.Null);
        Assert.That(error.ToString(), Does.Not.Contain(nameof(ODataHelperTests)));
    }

    [Test]
    public void UnhandledError_Keeps_Intended_ODataError_Message_When_Details_Disabled()
    {
        const string message = "Modifying or deleting a system customer role is not allowed.";
        var ex = new ODataErrorException(ODataHelper.CreateError(message, 403));

        var error = ODataHelper.CreateUnhandledError(Throw(ex), 500, showDetails: false);

        Assert.That(error.Message, Is.EqualTo(message));
        Assert.That(error.InnerError, Is.Null);
    }

    [Test]
    public void UnhandledError_Uses_Generic_Message_For_Empty_Exception_Message()
    {
        var error = ODataHelper.CreateUnhandledError(new ODataException(string.Empty), 401, showDetails: false);

        Assert.That(error.Message, Is.EqualTo(ODataHelper.UnknownErrorMessage));
        Assert.That(error.Code, Is.EqualTo("401"));
    }

    private static Exception Throw(Exception ex)
    {
        try
        {
            throw ex;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}
