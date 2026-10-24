using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace Messanger.Http;

internal static class ApiContractRegistration
{
    public static IServiceCollection AddApiContracts(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = "Messanger API";
                document.Info.Version = "v1";
                document.Info.Description = "Implemented HTTP contracts. SignalR method/event contracts are documented in README.md. Browser sessions use CSRF-protected HTTPS auth endpoints and a persistent refresh cookie.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                    Description = "RS256 access JWT from POST /auth/login. Refresh tokens are not accepted as access credentials."
                };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, cancellationToken) =>
            {
                if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
                    });
                }
                return Task.CompletedTask;
            });
        });
        return services;
    }

    public static RouteHandlerBuilder WithApiErrors(this RouteHandlerBuilder endpoint)
    {
        foreach (var status in new[] { 400, 401, 403, 404, 409, 413, 415, 422, 429, 500 })
            endpoint.Produces<ApiErrorResponse>(status);
        return endpoint;
    }
}
