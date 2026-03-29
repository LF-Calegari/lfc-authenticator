namespace AuthService.Auth;

public interface IPermissionResolver
{
    /// <summary>Resolve uma chave oficial (ex.: Users.Create) para o Id da permissão no banco.</summary>
    Task<Guid?> ResolveToIdAsync(string key, CancellationToken cancellationToken = default);
}
