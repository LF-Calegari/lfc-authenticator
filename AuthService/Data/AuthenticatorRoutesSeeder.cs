using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Garante o catálogo de rotas autenticadas do sistema Authenticator (idempotente).</summary>
public static class AuthenticatorRoutesSeeder
{
    private const string SystemCode = "authenticator";

    private static readonly (string Code, string Name, string Description)[] Routes =
    [
        // Auth
        ("AUTH_V1_AUTH_VERIFY_TOKEN", "GET /api/v1/auth/verify-token",
            "Valida o JWT atual e a autorização do usuário para a rota indicada no header X-Route-Code."),
        ("AUTH_V1_AUTH_PERMISSIONS", "GET /api/v1/auth/permissions",
            "Retorna o catálogo de permissões e rotas autorizadas do usuário no sistema do header X-System-Id."),
        ("AUTH_V1_AUTH_LOGOUT", "GET /api/v1/auth/logout",
            "Encerra a sessão do usuário invalidando JWTs anteriores via incremento do TokenVersion."),

        // Clients
        ("AUTH_V1_CLIENTS_CREATE", "POST /api/v1/clients",
            "Cria um novo cliente (PF ou PJ)."),
        ("AUTH_V1_CLIENTS_LIST", "GET /api/v1/clients",
            "Lista clientes ativos."),
        ("AUTH_V1_CLIENTS_GET_BY_ID", "GET /api/v1/clients/{id}",
            "Obtém um cliente pelo Id."),
        ("AUTH_V1_CLIENTS_UPDATE", "PUT /api/v1/clients/{id}",
            "Atualiza dados cadastrais do cliente."),
        ("AUTH_V1_CLIENTS_DELETE", "DELETE /api/v1/clients/{id}",
            "Remove (soft-delete) um cliente."),
        ("AUTH_V1_CLIENTS_RESTORE", "POST /api/v1/clients/{id}/restore",
            "Restaura um cliente previamente removido."),
        ("AUTH_V1_CLIENTS_EMAILS_CREATE", "POST /api/v1/clients/{id}/emails",
            "Adiciona um email ao cliente."),
        ("AUTH_V1_CLIENTS_EMAILS_DELETE", "DELETE /api/v1/clients/{id}/emails/{emailId}",
            "Remove um email do cliente."),
        ("AUTH_V1_CLIENTS_MOBILES_CREATE", "POST /api/v1/clients/{id}/mobiles",
            "Adiciona um celular ao cliente (limite de 3)."),
        ("AUTH_V1_CLIENTS_MOBILES_DELETE", "DELETE /api/v1/clients/{id}/mobiles/{phoneId}",
            "Remove um celular do cliente."),
        ("AUTH_V1_CLIENTS_PHONES_CREATE", "POST /api/v1/clients/{id}/phones",
            "Adiciona um telefone fixo ao cliente (limite de 3)."),
        ("AUTH_V1_CLIENTS_PHONES_DELETE", "DELETE /api/v1/clients/{id}/phones/{phoneId}",
            "Remove um telefone fixo do cliente."),

        // Permissions
        ("AUTH_V1_PERMISSIONS_CREATE", "POST /api/v1/permissions",
            "Cria uma nova permissão (vincula uma rota a um tipo de permissão)."),
        ("AUTH_V1_PERMISSIONS_LIST", "GET /api/v1/permissions",
            "Lista permissões ativas."),
        ("AUTH_V1_PERMISSIONS_GET_BY_ID", "GET /api/v1/permissions/{id}",
            "Obtém uma permissão pelo Id."),
        ("AUTH_V1_PERMISSIONS_UPDATE", "PUT /api/v1/permissions/{id}",
            "Atualiza uma permissão existente."),
        ("AUTH_V1_PERMISSIONS_DELETE", "DELETE /api/v1/permissions/{id}",
            "Remove (soft-delete) uma permissão."),
        ("AUTH_V1_PERMISSIONS_RESTORE", "POST /api/v1/permissions/{id}/restore",
            "Restaura uma permissão previamente removida."),

        // PermissionTypes
        ("AUTH_V1_PERMISSION_TYPES_CREATE", "POST /api/v1/permissions/types",
            "Cria um novo tipo de permissão."),
        ("AUTH_V1_PERMISSION_TYPES_LIST", "GET /api/v1/permissions/types",
            "Lista tipos de permissão ativos."),
        ("AUTH_V1_PERMISSION_TYPES_GET_BY_ID", "GET /api/v1/permissions/types/{id}",
            "Obtém um tipo de permissão pelo Id."),
        ("AUTH_V1_PERMISSION_TYPES_UPDATE", "PUT /api/v1/permissions/types/{id}",
            "Atualiza um tipo de permissão."),
        ("AUTH_V1_PERMISSION_TYPES_DELETE", "DELETE /api/v1/permissions/types/{id}",
            "Remove (soft-delete) um tipo de permissão."),
        ("AUTH_V1_PERMISSION_TYPES_RESTORE", "POST /api/v1/permissions/types/{id}/restore",
            "Restaura um tipo de permissão previamente removido."),

        // Roles
        ("AUTH_V1_ROLES_CREATE", "POST /api/v1/roles",
            "Cria uma nova role."),
        ("AUTH_V1_ROLES_LIST", "GET /api/v1/roles",
            "Lista roles ativas."),
        ("AUTH_V1_ROLES_GET_BY_ID", "GET /api/v1/roles/{id}",
            "Obtém uma role pelo Id."),
        ("AUTH_V1_ROLES_UPDATE", "PUT /api/v1/roles/{id}",
            "Atualiza uma role existente."),
        ("AUTH_V1_ROLES_DELETE", "DELETE /api/v1/roles/{id}",
            "Remove (soft-delete) uma role."),
        ("AUTH_V1_ROLES_RESTORE", "POST /api/v1/roles/{id}/restore",
            "Restaura uma role previamente removida."),
        ("AUTH_V1_ROLES_PERMISSIONS_ASSIGN", "POST /api/v1/roles/{id}/permissions",
            "Vincula uma permissão à role."),
        ("AUTH_V1_ROLES_PERMISSIONS_REMOVE", "DELETE /api/v1/roles/{id}/permissions/{permissionId}",
            "Remove o vínculo entre uma permissão e a role."),

        // Routes
        ("AUTH_V1_SYSTEMS_ROUTES_CREATE", "POST /api/v1/systems/routes",
            "Cria uma nova rota no catálogo do sistema."),
        ("AUTH_V1_SYSTEMS_ROUTES_LIST", "GET /api/v1/systems/routes",
            "Lista rotas ativas do catálogo."),
        ("AUTH_V1_SYSTEMS_ROUTES_GET_BY_ID", "GET /api/v1/systems/routes/{id}",
            "Obtém uma rota pelo Id."),
        ("AUTH_V1_SYSTEMS_ROUTES_UPDATE", "PUT /api/v1/systems/routes/{id}",
            "Atualiza uma rota existente."),
        ("AUTH_V1_SYSTEMS_ROUTES_DELETE", "DELETE /api/v1/systems/routes/{id}",
            "Remove (soft-delete) uma rota."),
        ("AUTH_V1_SYSTEMS_ROUTES_RESTORE", "POST /api/v1/systems/routes/{id}/restore",
            "Restaura uma rota previamente removida."),
        ("AUTH_V1_SYSTEMS_ROUTES_SYNC", "POST /api/v1/systems/routes/sync",
            "Sincroniza em lote o catálogo de rotas de um sistema (auto-registro pelo sistema-cliente)."),

        // Systems
        ("AUTH_V1_SYSTEMS_CREATE", "POST /api/v1/systems",
            "Cadastra um novo sistema oficial."),
        ("AUTH_V1_SYSTEMS_LIST", "GET /api/v1/systems",
            "Lista sistemas ativos."),
        ("AUTH_V1_SYSTEMS_GET_BY_ID", "GET /api/v1/systems/{id}",
            "Obtém um sistema pelo Id."),
        ("AUTH_V1_SYSTEMS_UPDATE", "PUT /api/v1/systems/{id}",
            "Atualiza um sistema existente."),
        ("AUTH_V1_SYSTEMS_DELETE", "DELETE /api/v1/systems/{id}",
            "Remove (soft-delete) um sistema."),
        ("AUTH_V1_SYSTEMS_RESTORE", "POST /api/v1/systems/{id}/restore",
            "Restaura um sistema previamente removido."),

        // TokenTypes
        ("AUTH_V1_TOKEN_TYPES_CREATE", "POST /api/v1/tokens/types",
            "Cria um novo tipo de token."),
        ("AUTH_V1_TOKEN_TYPES_LIST", "GET /api/v1/tokens/types",
            "Lista tipos de token ativos."),
        ("AUTH_V1_TOKEN_TYPES_GET_BY_ID", "GET /api/v1/tokens/types/{id}",
            "Obtém um tipo de token pelo Id."),
        ("AUTH_V1_TOKEN_TYPES_UPDATE", "PUT /api/v1/tokens/types/{id}",
            "Atualiza um tipo de token."),
        ("AUTH_V1_TOKEN_TYPES_DELETE", "DELETE /api/v1/tokens/types/{id}",
            "Remove (soft-delete) um tipo de token."),
        ("AUTH_V1_TOKEN_TYPES_RESTORE", "POST /api/v1/tokens/types/{id}/restore",
            "Restaura um tipo de token previamente removido."),

        // Users
        ("AUTH_V1_USERS_CREATE", "POST /api/v1/users",
            "Cria um novo usuário."),
        ("AUTH_V1_USERS_LIST", "GET /api/v1/users",
            "Lista usuários ativos."),
        ("AUTH_V1_USERS_GET_BY_ID", "GET /api/v1/users/{id}",
            "Obtém um usuário pelo Id."),
        ("AUTH_V1_USERS_UPDATE", "PUT /api/v1/users/{id}",
            "Atualiza dados cadastrais do usuário."),
        ("AUTH_V1_USERS_UPDATE_PASSWORD", "PUT /api/v1/users/{id}/password",
            "Atualiza a senha do usuário."),
        ("AUTH_V1_USERS_DELETE", "DELETE /api/v1/users/{id}",
            "Remove (soft-delete) um usuário."),
        ("AUTH_V1_USERS_RESTORE", "POST /api/v1/users/{id}/restore",
            "Restaura um usuário previamente removido."),
        ("AUTH_V1_USERS_PERMISSIONS_ASSIGN", "POST /api/v1/users/{id}/permissions",
            "Vincula uma permissão diretamente ao usuário."),
        ("AUTH_V1_USERS_PERMISSIONS_REMOVE", "DELETE /api/v1/users/{id}/permissions/{permissionId}",
            "Remove o vínculo direto de uma permissão com o usuário."),
        ("AUTH_V1_USERS_ROLES_ASSIGN", "POST /api/v1/users/{id}/roles",
            "Vincula uma role ao usuário."),
        ("AUTH_V1_USERS_ROLES_REMOVE", "DELETE /api/v1/users/{id}/roles/{roleId}",
            "Remove o vínculo de uma role com o usuário.")
    ];

    public static async Task EnsureRoutesAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var systemId = await db.Systems.AsNoTracking()
            .Where(s => s.Code == SystemCode)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (systemId is null || systemId == Guid.Empty)
            throw new InvalidOperationException(
                $"Sistema '{SystemCode}' não encontrado. Execute o SystemSeeder antes do AuthenticatorRoutesSeeder.");

        var defaultTokenTypeId = await db.SystemTokenTypes.AsNoTracking()
            .Where(t => t.Code == SystemTokenTypeSeeder.DefaultCode)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultTokenTypeId is null || defaultTokenTypeId == Guid.Empty)
            throw new InvalidOperationException(
                $"SystemTokenType '{SystemTokenTypeSeeder.DefaultCode}' não encontrado. Execute o SystemTokenTypeSeeder antes do AuthenticatorRoutesSeeder.");

        var utc = DateTime.UtcNow;

        foreach (var (code, name, description) in Routes)
        {
            var existing = await db.Routes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Code == code, cancellationToken);

            if (existing is null)
            {
                db.Routes.Add(new AppRoute
                {
                    SystemId = systemId.Value,
                    Code = code,
                    Name = name,
                    Description = description,
                    SystemTokenTypeId = defaultTokenTypeId.Value,
                    CreatedAt = utc,
                    UpdatedAt = utc,
                    DeletedAt = null
                });
                continue;
            }

            existing.SystemId = systemId.Value;
            existing.Name = name;
            existing.Description = description;
            existing.SystemTokenTypeId = defaultTokenTypeId.Value;
            if (existing.DeletedAt is not null)
                existing.DeletedAt = null;
            existing.UpdatedAt = utc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
