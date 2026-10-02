using Messanger.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Messanger.Http;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var error = exception switch
        {
            UseCaseException { Code: "forbidden" } forbidden => (403, forbidden.Code, forbidden.Message),
            UseCaseException { Code: "not_found" } missing => (404, missing.Code, missing.Message),
            UseCaseException { Code: "validation_failed" } validation => (422, validation.Code, validation.Message),
            UseCaseException { Code: "invalid_credentials" } credentials =>
                (401, credentials.Code, credentials.Message),
            UseCaseException { Code: "username_taken" } conflict => (409, conflict.Code, conflict.Message),
            DbUpdateException
            {
                InnerException: PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: "IX_users_normalized_username"
                }
            } => (409, "username_taken", "Username is already taken."),
            BadHttpRequestException badRequest => (badRequest.StatusCode,
                ApiError.ForStatus(badRequest.StatusCode).Code, ApiError.ForStatus(badRequest.StatusCode).Message),
            _ => (500, "internal_error", "An unexpected error occurred.")
        };
        if (error.Item1 == 500)
            logger.LogError(exception, "Unhandled request failure {RequestId}", context.TraceIdentifier);
        context.Response.Headers.CacheControl = "no-store";
        await ApiError.WriteAsync(context, error.Item1, error.Item2, error.Item3);
        return true;
    }
}