using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

public static class LegacyUsersClientSeeder
{
    /// <summary>
    /// Migra usuários legados sem ClientId para o novo domínio, gerando clientes PF válidos.
    /// </summary>
    public static async Task EnsureLegacyUsersHaveClientAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var usersWithoutClient = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.ClientId == null)
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .ToListAsync(cancellationToken);

        if (usersWithoutClient.Count == 0)
            return;

        var usedCpfs = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => c.Cpf != null)
            .Select(c => c.Cpf!)
            .ToHashSetAsync(cancellationToken);

        var ordinal = usedCpfs.Count + 1;
        foreach (var user in usersWithoutClient)
        {
            var client = LegacyClientFactory.BuildPfClientForUser(user, usedCpfs, ordinal++);
            db.Clients.Add(client);
            user.ClientId = client.Id;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
