using System.Text.Json;
using AuthService.Controllers.Auth;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AuthService.OpenApi;

public sealed class ContractExamplesOperationFilter : IOperationFilter
{
    private static readonly string[] ExamplePermissions = ["11111111-1111-1111-1111-111111111111"];
    private static readonly string[] ExamplePermissionCodes = ["perm:Users.Read", "perm:Roles.Read"];
    private static readonly string[] ExampleRouteCodes = ["KURTTO_V1_URLS_LIST_INCLUDE_DELETED"];

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        AddErrorResponse(operation, "400", "Requisicao invalida.", new { message = "Payload invalido." });
        AddErrorResponse(operation, "401", "Nao autenticado.", new { message = "Token invalido." });
        AddErrorResponse(operation, "403", "Sem permissao.", new { message = "Acesso negado." });
        AddErrorResponse(operation, "404", "Recurso nao encontrado.", new { message = "Recurso nao encontrado." });
        AddErrorResponse(operation, "409", "Conflito de dados.", new { message = "Conflito de unicidade." });

        var apiPath = context.ApiDescription.RelativePath ?? string.Empty;
        var normalized = "/" + apiPath.TrimStart('/').ToLowerInvariant();
        var method = context.ApiDescription.HttpMethod?.ToUpperInvariant() ?? string.Empty;

        if (normalized == "/health" && method == "GET")
        {
            AddJsonResponse(operation, "200", "Health check.", new { status = "ok", message = "API is running" });
            return;
        }

        if (normalized == "/auth/login" && method == "POST")
        {
            AddJsonResponse(operation, "200", "Login realizado.", new { token = "<jwt-token>" });
            AddErrorResponse(operation, "400", "SystemId ausente ou sistema invalido/inativo.", new { message = "SystemId é obrigatório." });
            AddErrorResponse(operation, "401", "Credenciais invalidas.", new { message = "Credenciais inválidas." });
            return;
        }

        if (normalized == "/auth/verify-token" && method == "GET")
        {
            EnsureSystemIdHeaderParameter(operation);
            AddJsonResponse(operation, "200", "Token valido.", new
            {
                id = "00000000-0000-0000-0000-000000000000",
                name = "User Name",
                email = "user@example.com",
                identity = 1,
                permissions = ExamplePermissions,
                permissionCodes = ExamplePermissionCodes,
                routeCodes = ExampleRouteCodes
            });
            AddErrorResponse(operation, "400", "Header X-System-Id ausente ou sistema invalido/inativo.", new { message = "Header X-System-Id é obrigatório." });
            return;
        }

        if (normalized == "/auth/logout" && method == "GET")
        {
            AddJsonResponse(operation, "200", "Logout concluido.", new { message = "Sessão encerrada." });
            return;
        }

        if (normalized.EndsWith("/restore", StringComparison.Ordinal) && method == "POST")
        {
            AddJsonResponse(operation, "200", "Restauracao concluida.", new { message = "Registro restaurado com sucesso." });
            return;
        }

        if (method == "POST")
        {
            AddJsonResponse(operation, "201", "Registro criado.", new { id = "00000000-0000-0000-0000-000000000000" });
            return;
        }

        if (method == "GET")
        {
            AddJsonResponse(operation, "200", "Leitura concluida.", new { message = "Consulta realizada com sucesso." });
            return;
        }

        if (method == "PUT")
        {
            AddJsonResponse(operation, "200", "Atualizacao concluida.", new { message = "Registro atualizado com sucesso." });
            return;
        }

        if (method == "DELETE")
        {
            AddNoContentResponse(operation);
        }
    }

    private static void AddNoContentResponse(OpenApiOperation operation)
    {
        var responses = operation.Responses;
        if (responses is null)
            return;

        responses["204"] = new OpenApiResponse
        {
            Description = "Remocao concluida sem conteudo."
        };
    }

    private static void AddErrorResponse(OpenApiOperation operation, string statusCode, string description, object example)
    {
        if (statusCode is not ("400" or "401" or "403" or "404" or "409"))
            return;

        var responses = operation.Responses;
        if (responses is null)
            return;

        responses[statusCode] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Example = JsonSerializer.Serialize(example)
                }
            }
        };
    }

    private static void AddJsonResponse(OpenApiOperation operation, string statusCode, string description, object example)
    {
        var responses = operation.Responses;
        if (responses is null)
            return;

        responses[statusCode] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Example = JsonSerializer.Serialize(example)
                }
            }
        };
    }

    private static void EnsureSystemIdHeaderParameter(OpenApiOperation operation)
    {
        // O parâmetro X-System-Id é gerado automaticamente pelo ASP.NET Core a partir do
        // [FromHeader] no controller. Aqui só ajustamos metadados (required, description)
        // para garantir uma documentação consistente no Swagger.
        const string headerDescription =
            "UUID do sistema cliente. Deve corresponder ao systemId usado em POST /auth/login.";

        operation.Parameters ??= [];
        var existing = operation.Parameters.FirstOrDefault(p =>
            p.In == ParameterLocation.Header
            && string.Equals(p.Name, AuthController.SystemIdHeader, StringComparison.OrdinalIgnoreCase));

        if (existing is OpenApiParameter mutable)
        {
            mutable.Required = true;
            if (string.IsNullOrWhiteSpace(mutable.Description))
                mutable.Description = headerDescription;
            return;
        }

        if (existing is not null)
        {
            // Parâmetro presente como referência imutável; deixamos o auto-gerado prevalecer
            // sem duplicar a entrada na operação.
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = AuthController.SystemIdHeader,
            In = ParameterLocation.Header,
            Required = true,
            Description = headerDescription
        });
    }
}
