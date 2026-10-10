namespace Messanger.Http;

public static class ApiError
{
    public static Task WriteAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(
            new ApiErrorResponse(new ApiErrorDetails(code, message, context.TraceIdentifier)), context.RequestAborted);
    }

    public static (string Code, string Message) ForStatus(int statusCode)
    {
        return statusCode switch
        {
            400 => ("bad_request", "The request is malformed."),
            401 => ("unauthorized", "A valid access token is required."),
            403 => ("forbidden", "You do not have access to this resource."),
            404 => ("not_found", "The resource was not found."),
            405 => ("method_not_allowed", "This HTTP method is not allowed."),
            413 => ("payload_too_large", "The request body is too large."),
            415 => ("unsupported_media_type", "The content type is not supported."),
            _ => ("request_failed", "The request could not be completed.")
        };
    }
}