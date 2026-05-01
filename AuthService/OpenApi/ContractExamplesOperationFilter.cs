using System.Text.Json;
using AuthService.Controllers.Auth;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AuthService.OpenApi;

public sealed class ContractExamplesOperationFilter : IOperationFilter
{
    private static readonly string[] ExampleRoutes = ["AUTH_V1_USERS_LIST"];

    /// <summary>Timestamp ilustrativo reutilizado nos exemplos OpenAPI (CreatedAt/UpdatedAt/issuedAt).</summary>
    private const string ExampleTimestamp = "2026-04-26T18:00:00+00:00";

    /// <summary>Guid placeholder reutilizado em ids de exemplo nas respostas OpenAPI.</summary>
    private const string EmptyGuidExample = "00000000-0000-0000-0000-000000000000";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        AddDefaultErrorResponses(operation);

        var apiPath = context.ApiDescription.RelativePath ?? string.Empty;
        var normalized = "/" + apiPath.TrimStart('/').ToLowerInvariant();
        var method = context.ApiDescription.HttpMethod?.ToUpperInvariant() ?? string.Empty;

        if (TryApplyEndpointSpecificResponses(operation, normalized, method))
            return;

        ApplyMethodFallbackResponse(operation, normalized, method);
    }

    private static void AddDefaultErrorResponses(OpenApiOperation operation)
    {
        AddErrorResponse(operation, "400", "Requisicao invalida.", new { message = "Payload invalido." });
        AddErrorResponse(operation, "401", "Nao autenticado.", new { message = "Token invalido." });
        AddErrorResponse(operation, "403", "Sem permissao.", new { message = "Acesso negado." });
        AddErrorResponse(operation, "404", "Recurso nao encontrado.", new { message = "Recurso nao encontrado." });
        AddErrorResponse(operation, "409", "Conflito de dados.", new { message = "Conflito de unicidade." });
    }

    /// <summary>
    /// Aplica os exemplos OpenAPI de endpoints específicos (rota+verbo). Retorna <c>true</c>
    /// quando o endpoint atual foi tratado por algum dos helpers, sinalizando ao chamador
    /// que não há fallback por método HTTP a aplicar.
    /// </summary>
    private static bool TryApplyEndpointSpecificResponses(OpenApiOperation operation, string normalizedPath, string method)
    {
        return TryApplyHealthGet(operation, normalizedPath, method)
            || TryApplyAuthLoginPost(operation, normalizedPath, method)
            || TryApplyAuthVerifyTokenGet(operation, normalizedPath, method)
            || TryApplyAuthPermissionsGet(operation, normalizedPath, method)
            || TryApplyAuthLogoutGet(operation, normalizedPath, method)
            || TryApplySystemsListGet(operation, normalizedPath, method)
            || TryApplyRoutesListGet(operation, normalizedPath, method)
            || TryApplyRoutesDelete(operation, normalizedPath, method)
            || TryApplyUsersForceLogoutPost(operation, normalizedPath, method)
            || TryApplyRestorePost(operation, normalizedPath, method);
    }

    private static bool TryApplyHealthGet(OpenApiOperation operation, string normalizedPath, string method)
    {
        if (normalizedPath != "/health" || method != "GET")
            return false;

        AddJsonResponse(operation, "200", "Health check.", new { status = "ok", message = "API is running" });
        return true;
    }

    private static bool TryApplyAuthLoginPost(OpenApiOperation operation, string normalizedPath, string method)
    {
        if (normalizedPath != "/auth/login" || method != "POST")
            return false;

        AddJsonResponse(operation, "200", "Login realizado.", new { token = "<jwt-token>" });
        AddErrorResponse(operation, "400", "SystemId ausente ou sistema invalido/inativo.", new { message = "SystemId é obrigatório." });
        AddErrorResponse(operation, "401", "Credenciais invalidas.", new { message = "Credenciais inválidas." });
        return true;
    }

    private static bool TryApplyAuthVerifyTokenGet(OpenApiOperation operation, string normalizedPath, string method)
    {
        if (normalizedPath != "/auth/verify-token" || method != "GET")
            return false;

        EnsureSystemIdHeaderParameter(operation);
        EnsureRouteCodeHeaderParameter(operation);
        AddJsonResponse(operation, "200", "Token valido e rota autorizada.", new
        {
            valid = true,
            issuedAt = ExampleTimestamp,
            expiresAt = "2026-04-26T19:00:00+00:00"
        });
        AddErrorResponse(
            operation,
            "400",
            "Header X-System-Id/X-Route-Code ausente, sistema invalido/inativo ou rota desconhecida.",
            new { message = "Header X-Route-Code é obrigatório." });
        AddErrorResponse(operation, "403", "Token valido, mas usuario sem direito a rota informada.", new { message = "Acesso negado para a rota." });
        return true;
    }

    private static bool TryApplyAuthPermissionsGet(OpenApiOperation operation, string normalizedPath, string method)
    {
        if (normalizedPath != "/auth/permissions" || method != "GET")
            return false;

        EnsureSystemIdHeaderParameter(operation);
        AddJsonResponse(operation, "200", "Catalogo de rotas do sistema solicitado.", new
        {
            user = new
            {
                id = EmptyGuidExample,
                name = "User Name",
                email = "user@example.com",
                identity = 1
            },
            routes = ExampleRoutes
        });
        AddErrorResponse(operation, "400", "Header X-System-Id ausente ou sistema invalido/inativo.", new { message = "Header X-System-Id é obrigatório." });
        return true;
    }

    private static bool TryApplyAuthLogoutGet(OpenApiOperation operation, string normalizedPath, string method)
    {
        if (normalizedPath != "/auth/logout" || method != "GET")
            return false;

        AddJsonResponse(operation, "200", "Logout concluido.", new { message = "Sessão encerrada." });
        return true;
    }

    private static bool TryApplyRestorePost(OpenApiOperation operation, string normalizedPath, string method)
    {
        if (method != "POST" || !normalizedPath.EndsWith("/restore", StringComparison.Ordinal))
            return false;

        AddJsonResponse(operation, "200", "Restauracao concluida.", new { message = "Registro restaurado com sucesso." });
        return true;
    }

    private static bool TryApplyUsersForceLogoutPost(OpenApiOperation operation, string normalizedPath, string method)
    {
        // POST /users/{id}/force-logout (issue #168): admin invalida sessões ativas do usuário-alvo
        // incrementando o TokenVersion. Self-target retorna 400, soft-deleted/inexistente retorna 404.
        if (method != "POST" || !normalizedPath.StartsWith("/users/", StringComparison.Ordinal))
            return false;
        if (!normalizedPath.EndsWith("/force-logout", StringComparison.Ordinal))
            return false;

        AddJsonResponse(operation, "200", "Sessoes do usuario invalidadas com sucesso.", new
        {
            message = "Sessões do usuário invalidadas com sucesso.",
            userId = EmptyGuidExample,
            newTokenVersion = 1
        });
        AddErrorResponse(
            operation,
            "400",
            "Caller tentou forcar logout de si mesmo (use GET /auth/logout em vez disso).",
            new { message = "Não é possível forçar logout de si mesmo por este endpoint. Utilize GET /auth/logout." });
        AddErrorResponse(
            operation,
            "404",
            "Usuario nao encontrado ou soft-deletado.",
            new { message = "Usuário não encontrado." });
        return true;
    }

    private static bool TryApplyRoutesDelete(OpenApiOperation operation, string normalizedPath, string method)
    {
        // DELETE /systems/routes/{id} (issue #157): documentar o 409 com payload
        // { message, linkedPermissionsCount } quando há Permissions ativas vinculadas.
        if (method != "DELETE" || !normalizedPath.StartsWith("/systems/routes/", StringComparison.Ordinal))
            return false;
        if (normalizedPath.EndsWith("/restore", StringComparison.Ordinal))
            return false;

        AddNoContentResponse(operation);
        AddErrorResponse(
            operation,
            "404",
            "Route nao encontrada.",
            new { message = "Route não encontrada." });
        AddErrorResponse(
            operation,
            "409",
            "Route possui Permissions ativas vinculadas; remova as permissoes antes de excluir.",
            new
            {
                message = "Não é possível excluir a rota: existem permissões ativas vinculadas. Remova as permissões antes.",
                linkedPermissionsCount = 1
            });
        return true;
    }

    private static bool TryApplySystemsListGet(OpenApiOperation operation, string normalizedPath, string method)
    {
        if (normalizedPath != "/systems" || method != "GET")
            return false;

        EnsureSystemsListQueryParameters(operation);

        AddJsonResponse(operation, "200", "Pagina de sistemas que casam com os filtros aplicados.", new
        {
            data = new[]
            {
                new
                {
                    id = EmptyGuidExample,
                    name = "Admin GUI",
                    code = "ADMIN_GUI",
                    description = "Painel administrativo.",
                    createdAt = ExampleTimestamp,
                    updatedAt = ExampleTimestamp,
                    deletedAt = (string?)null
                }
            },
            page = 1,
            pageSize = 20,
            total = 1
        });
        AddErrorResponse(
            operation,
            "400",
            "Parametros de paginacao invalidos (page <= 0 ou pageSize fora do intervalo permitido).",
            new { message = "pageSize deve estar entre 1 e 100." });
        return true;
    }

    private static void EnsureSystemsListQueryParameters(OpenApiOperation operation)
    {
        EnsureQueryParameterDescription(
            operation,
            "q",
            "Termo de busca case-insensitive em Name e Code (matching parcial via ILIKE).");
        EnsureQueryParameterDescription(
            operation,
            "page",
            "Numero da pagina (1-based, default 1). Valores <= 0 retornam 400.");
        EnsureQueryParameterDescription(
            operation,
            "pageSize",
            "Tamanho da pagina (default 20, maximo 100). Valores <= 0 ou > 100 retornam 400.");
        EnsureQueryParameterDescription(
            operation,
            "includeDeleted",
            "Quando true, inclui registros soft-deleted (DeletedAt != null). Default false.");
    }

    private static bool TryApplyRoutesListGet(OpenApiOperation operation, string normalizedPath, string method)
    {
        if (normalizedPath != "/systems/routes" || method != "GET")
            return false;

        EnsureRoutesListQueryParameters(operation);

        AddJsonResponse(operation, "200", "Pagina de rotas que casam com os filtros aplicados.", new
        {
            data = new[]
            {
                new
                {
                    id = EmptyGuidExample,
                    systemId = "11111111-1111-1111-1111-111111111111",
                    name = "Listar usuarios",
                    code = "AUTH_V1_USERS_LIST",
                    description = (string?)null,
                    systemTokenTypeId = "22222222-2222-2222-2222-222222222222",
                    systemTokenTypeCode = "default",
                    systemTokenTypeName = "Default",
                    createdAt = ExampleTimestamp,
                    updatedAt = ExampleTimestamp,
                    deletedAt = (string?)null
                }
            },
            page = 1,
            pageSize = 20,
            total = 1
        });
        AddErrorResponse(
            operation,
            "400",
            "Parametros de paginacao/filtro invalidos (page <= 0, pageSize fora do intervalo permitido ou systemId = Guid.Empty).",
            new { message = "pageSize deve estar entre 1 e 100." });
        return true;
    }

    private static void EnsureRoutesListQueryParameters(OpenApiOperation operation)
    {
        EnsureQueryParameterDescription(
            operation,
            "systemId",
            "Quando informado, restringe a listagem as rotas do sistema indicado. systemId = Guid.Empty retorna 400.");
        EnsureQueryParameterDescription(
            operation,
            "q",
            "Termo de busca case-insensitive em Code e Name (matching parcial via ILIKE; %, _ e \\ sao tratados como literais).");
        EnsureQueryParameterDescription(
            operation,
            "page",
            "Numero da pagina (1-based, default 1). Valores <= 0 retornam 400.");
        EnsureQueryParameterDescription(
            operation,
            "pageSize",
            "Tamanho da pagina (default 20, maximo 100). Valores <= 0 ou > 100 retornam 400.");
        EnsureQueryParameterDescription(
            operation,
            "includeDeleted",
            "Quando true, inclui rotas soft-deletadas e rotas cujo sistema pai foi soft-deletado (cenario admin). Default false.");
    }

    private static void EnsureQueryParameterDescription(OpenApiOperation operation, string parameterName, string description)
    {
        operation.Parameters ??= [];
        var existing = operation.Parameters.FirstOrDefault(p =>
            p.In == ParameterLocation.Query
            && string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));

        if (existing is OpenApiParameter mutable && string.IsNullOrWhiteSpace(mutable.Description))
            mutable.Description = description;
    }

    /// <summary>
    /// Fallback genérico por verbo HTTP para endpoints que não foram cobertos por um
    /// helper específico em <see cref="TryApplyEndpointSpecificResponses"/>.
    /// </summary>
    private static void ApplyMethodFallbackResponse(OpenApiOperation operation, string normalizedPath, string method)
    {
        // normalizedPath é mantido na assinatura para coerência com os helpers específicos;
        // os fallbacks por verbo não dependem do path concreto.
        _ = normalizedPath;

        switch (method)
        {
            case "POST":
                AddJsonResponse(operation, "201", "Registro criado.", new { id = EmptyGuidExample });
                break;
            case "GET":
                AddJsonResponse(operation, "200", "Leitura concluida.", new { message = "Consulta realizada com sucesso." });
                break;
            case "PUT":
                AddJsonResponse(operation, "200", "Atualizacao concluida.", new { message = "Registro atualizado com sucesso." });
                break;
            case "DELETE":
                AddNoContentResponse(operation);
                break;
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

        EnsureRequiredHeaderParameter(operation, AuthController.SystemIdHeader, headerDescription);
    }

    private static void EnsureRouteCodeHeaderParameter(OpenApiOperation operation)
    {
        // Parâmetro também gerado automaticamente via [FromHeader] no controller. Aqui apenas
        // garantimos que o header aparece como obrigatório no Swagger e com descrição clara.
        const string headerDescription =
            "Código da rota concreta a ser autorizada (ex.: AUTH_V1_USERS_LIST). "
            + "Deve estar entre as routes do usuário no sistema do header X-System-Id.";

        EnsureRequiredHeaderParameter(operation, AuthController.RouteCodeHeader, headerDescription);
    }

    private static void EnsureRequiredHeaderParameter(OpenApiOperation operation, string headerName, string headerDescription)
    {
        operation.Parameters ??= [];
        var existing = operation.Parameters.FirstOrDefault(p =>
            p.In == ParameterLocation.Header
            && string.Equals(p.Name, headerName, StringComparison.OrdinalIgnoreCase));

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
            Name = headerName,
            In = ParameterLocation.Header,
            Required = true,
            Description = headerDescription
        });
    }
}
