using AuthService.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthService.Tests;

public class PermissionAuthorizationPolicyProviderUnitTests
{
    [Fact]
    public async Task GetPolicyAsync_WithPermPrefix_BuildsPolicyWithRequirementAndBearerScheme()
    {
        var options = Options.Create(new AuthorizationOptions());
        var provider = new PermissionAuthorizationPolicyProvider(options);

        var policy = await provider.GetPolicyAsync("perm:Users.Read");

        Assert.NotNull(policy);
        Assert.Contains(BearerAuthenticationDefaults.AuthenticationScheme, policy.AuthenticationSchemes);
        var requirement = Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
        Assert.Equal("Users.Read", requirement.Key);
    }

    [Fact]
    public async Task GetPolicyAsync_WithoutPermPrefix_UsesFallbackPolicyProvider()
    {
        var authOptions = new AuthorizationOptions();
        authOptions.AddPolicy("custom", p => p.RequireAssertion(_ => true));
        var provider = new PermissionAuthorizationPolicyProvider(Options.Create(authOptions));

        var policy = await provider.GetPolicyAsync("custom");

        Assert.NotNull(policy);
        Assert.Empty(policy.Requirements.OfType<PermissionRequirement>());
    }
}
