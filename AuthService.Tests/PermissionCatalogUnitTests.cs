using AuthService.Auth;
using Xunit;

namespace AuthService.Tests;

public class PermissionCatalogUnitTests
{
    [Fact]
    public void GetResourcesForSystem_WithAuthenticator_ReturnsAllResourcesInOrdinalOrder()
    {
        var resources = PermissionCatalog.GetResourcesForSystem("authenticator");

        var expected = new[]
        {
            "Clients",
            "Permissions",
            "PermissionsTypes",
            "Roles",
            "SystemTokensTypes",
            "Systems",
            "SystemsRoutes",
            "Users"
        };

        Assert.Equal(expected, resources);
        Assert.Equal(8, resources.Count);
    }

    [Fact]
    public void GetResourcesForSystem_WithUnknownSystem_ReturnsEmptyArray()
    {
        var resources = PermissionCatalog.GetResourcesForSystem("inexistente");

        Assert.Same(Array.Empty<string>(), resources);
        Assert.Empty(resources);
    }
}
