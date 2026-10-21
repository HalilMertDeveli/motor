#nullable enable

using Microsoft.OData;

namespace Smartstore.Web.Api;

public static partial class ODataHelper
{
    /// <summary>
    /// The message returned instead of the message of an unexpected exception if exception details are hidden.
    /// </summary>
    public const string UnknownErrorMessage = "An unknown error has occurred.";

    /// <summary>
    /// Creates an OData error for an exception that was not handled by the API endpoint.
    /// </summary>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="statusCode">The HTTP status to return.</param>
    /// <param name="showDetails">
    /// A value indicating whether to include exception details like the type and stack trace.
    /// If <c>false</c>, the message of an unexpected exception is replaced by <see cref="UnknownErrorMessage"/>.
    /// Typically the value of <see cref="SmartConfiguration.ShowCustomErrors"/>.
    /// </param>
    public static ODataError CreateUnhandledError(Exception exception, int statusCode, bool showDetails)
    {
        Guard.NotNull(exception);

        // ODataException messages describe an invalid request (e.g. a malformed query) and are meant for the client.
        // ODataErrorException carries the intended client message in its error.
        var message = exception is ODataErrorException { Error.Message: var errorMessage } && errorMessage.HasValue()
            ? errorMessage
            : (showDetails || exception is ODataException ? exception.Message : null);

        return CreateError(message.NullEmpty() ?? UnknownErrorMessage, statusCode, showDetails ? exception : null);
    }
}
