using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AuthService.OpenApi;

/// <summary>
/// Prefixa paths no OpenAPI com /v1, alinhando ao <c>MapGroup("/v1")</c> (ApiExplorer não inclui o grupo).
/// </summary>
public sealed class V1PathPrefixDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var keys = swaggerDoc.Paths.Keys.ToList();
        foreach (var path in keys)
        {
            if (path.StartsWith("/v1", StringComparison.Ordinal))
                continue;
            if (!swaggerDoc.Paths.TryGetValue(path, out var item) || item is null)
                continue;
            swaggerDoc.Paths.Remove(path);
            swaggerDoc.Paths.Add("/v1" + path, item);
        }
    }
}
