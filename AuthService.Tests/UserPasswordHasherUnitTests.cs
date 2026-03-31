using AuthService.Models;
using AuthService.Security;
using Xunit;

namespace AuthService.Tests;

public class UserPasswordHasherUnitTests
{
    [Fact]
    public void HashPlainPassword_ThenVerify_ReturnsSuccessWithoutRehash()
    {
        var plainPassword = "StrongP@ssw0rd!";
        var user = new User
        {
            Password = UserPasswordHasher.HashPlainPassword(plainPassword)
        };

        var result = UserPasswordHasher.Verify(user, plainPassword);

        Assert.True(result.Success);
        Assert.Null(result.NewStoredPassword);
    }

    [Fact]
    public void Verify_WithLegacyPlainTextPassword_ReturnsSuccessAndRehash()
    {
        var plainPassword = "LegacyPass123";
        var user = new User
        {
            Password = plainPassword
        };

        var result = UserPasswordHasher.Verify(user, plainPassword);

        Assert.True(result.Success);
        Assert.NotNull(result.NewStoredPassword);
        Assert.NotEqual(plainPassword, result.NewStoredPassword);
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFailure()
    {
        var user = new User
        {
            Password = UserPasswordHasher.HashPlainPassword("Correct#123")
        };

        var result = UserPasswordHasher.Verify(user, "Wrong#123");

        Assert.False(result.Success);
        Assert.Null(result.NewStoredPassword);
    }
}
