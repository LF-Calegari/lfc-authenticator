using Microsoft.AspNetCore.Authorization;

namespace AuthService.Auth;

public sealed class PermissionRequirement(string key) : IAuthorizationRequirement
{
    public string Key { get; } = key;
}
