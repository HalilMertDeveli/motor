using System;
using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Localization;
using Smartstore.Core.Security;
using Smartstore.Core.Seo.Routing;
using Smartstore.Engine;
using Smartstore.Web.Controllers;
using Smartstore.Web.Models.Diagnostics;

namespace Smartstore.Web.Tests.Common.Controllers;

[TestFixture]
public class ErrorControllerTests
{
    const string SecretMessage = "Cannot open database \"MotorShop\" requested by the login.";
    const string LocalizedGenericMessage = "Ein unbekannter Fehler ist aufgetreten.";

    [Test]
    public void Ajax_Hides_Exception_Message_When_Custom_Errors_Disabled()
    {
        var model = ExecuteAjaxError(new InvalidOperationException(SecretMessage), showCustomErrors: false);

        Assert.That(model.ErrorMessage, Is.EqualTo(LocalizedGenericMessage));
        Assert.That(model.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public void Ajax_Shows_Exception_Message_When_Custom_Errors_Enabled()
    {
        var model = ExecuteAjaxError(new InvalidOperationException(SecretMessage), showCustomErrors: true);

        Assert.That(model.ErrorMessage, Is.EqualTo(SecretMessage));
    }

    [Test]
    public void Ajax_Keeps_User_Facing_Exception_Messages()
    {
        var accessDenied = ExecuteAjaxError(new AccessDeniedException("No permission."), showCustomErrors: false);
        Assert.That(accessDenied.ErrorMessage, Is.EqualTo("No permission."));

        var httpResponse = ExecuteAjaxError(new HttpResponseException(StatusCodes.Status429TooManyRequests, "Too many requests"), showCustomErrors: false);
        Assert.That(httpResponse.ErrorMessage, Is.EqualTo("Too many requests"));
    }

    [Test]
    public void Ajax_Falls_Back_To_English_When_Localization_Fails()
    {
        var model = ExecuteAjaxError(new InvalidOperationException(SecretMessage), showCustomErrors: false, localizationFails: true);

        Assert.That(model.ErrorMessage, Is.EqualTo("An unknown error has occurred."));
    }

    [Test]
    public void Ajax_Without_Exception_Has_No_Message()
    {
        var model = ExecuteAjaxError(null, showCustomErrors: false);

        Assert.That(model.ErrorMessage, Is.Null);
    }

    private static ErrorModel ExecuteAjaxError(Exception exception, bool showCustomErrors, bool localizationFails = false)
    {
        var appContext = new Mock<IApplicationContext>();
        appContext.SetupGet(x => x.AppConfiguration).Returns(new SmartConfiguration { ShowCustomErrors = showCustomErrors });

        var localizationService = new Mock<ILocalizationService>();
        var setup = localizationService.Setup(x => x.GetResource("Admin.Common.UnknownError", It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>()));
        if (localizationFails)
        {
            setup.Throws(new InvalidOperationException("No database."));
        }
        else
        {
            setup.Returns(LocalizedGenericMessage);
        }

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.SetUrlPolicy(new UrlPolicy(httpContext));

        if (exception != null)
        {
            httpContext.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Error = exception, Path = "/admin/product/productlist" });
        }

        var controller = new ErrorController(appContext.Object, new Lazy<ILocalizationService>(() => localizationService.Object))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = controller.Error(null);

        Assert.That(result, Is.InstanceOf<JsonResult>());
        return (ErrorModel)((JsonResult)result).Value;
    }
}
