using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FinanceControl.API.Infrastructure.Authentication;

/// <summary>
/// Declares the API key security scheme on the OpenAPI document so the Scalar
/// reference UI (and any generated client) knows that every <c>/api/*</c>
/// operation expects the <c>X-Api-Key</c> header. The scheme only documents
/// the contract — enforcement happens in <c>ApiKeyMiddleware</c>.
/// </summary>
public sealed class ApiKeySecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    /// <summary>Scheme id referenced by the document-wide security requirement.</summary>
    public const string SchemeName = "ApiKey";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeyOptions.HeaderName,
            In = ParameterLocation.Header,
            Description = "API key issued to the client. Every /api/* route returns 401 without it."
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = new List<string>()
        });

        return Task.CompletedTask;
    }
}
