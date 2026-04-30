using AuthService.Data;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Auth;

public sealed class PermissionResolver(AppDbContext db) : IPermissionResolver
{
    private readonly AppDbContext _db = db;

    public async Task<Guid?> ResolveToIdAsync(string key, CancellationToken cancellationToken = default)
    {
        var dot = key.LastIndexOf('.');
        if (dot <= 0 || dot >= key.Length - 1)
            return null;

        var resource = key[..dot];
        var action = key[(dot + 1)..];
        if (!PermissionCatalog.TryGetSystemCode(resource, out var systemCode))
            return null;

        var typeCode = action.ToLowerInvariant();

        var systemId = await _db.Systems.AsNoTracking()
            .Where(s => s.Code == systemCode)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (systemId is null)
            return null;

        var typeId = await _db.PermissionTypes.AsNoTracking()
            .Where(t => t.Code == typeCode)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (typeId is null)
            return null;

        // Adaptação mecânica pós-FK Permissions→Routes: como uma "ação no recurso" pode existir em
        // várias rotas do sistema, retornamos a primeira que casar. Redesign pendente do contrato de
        // /auth/permissions vai redefinir a granularidade efetiva.
        return await _db.Permissions.AsNoTracking()
            .Join(
                _db.Routes.AsNoTracking(),
                p => p.RouteId,
                r => r.Id,
                (p, r) => new { p.Id, p.PermissionTypeId, r.SystemId })
            .Where(x => x.SystemId == systemId && x.PermissionTypeId == typeId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
