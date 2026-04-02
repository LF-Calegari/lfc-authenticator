using System.Text.Json;
using AuthService.Auth;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthService.Tests;

public class JwtTokenServiceUnitTests
{
    [Fact]
    public void CreateAccessToken_WithShortSecret_ThrowsInvalidOperationException()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "short-secret",
            ExpirationMinutes = 30
        });
        var service = new JwtTokenService(options);

        Assert.Throws<InvalidOperationException>(() =>
        {
            service.CreateAccessToken(Guid.NewGuid(), 1, out _);
        });
    }

    [Fact]
    public void CreateAccessToken_WithNonPositiveExpiration_UsesDefault60Minutes()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "12345678901234567890123456789012",
            ExpirationMinutes = 0
        });
        var service = new JwtTokenService(options);
        var now = DateTimeOffset.UtcNow;

        _ = service.CreateAccessToken(Guid.NewGuid(), 3, out var expiresAt);

        var minutes = (expiresAt - now).TotalMinutes;
        Assert.InRange(minutes, 59, 61);
    }

    [Fact]
    public void CreateAccessToken_EmbedsExpectedClaims()
    {
        var userId = Guid.NewGuid();
        const int tokenVersion = 7;
        var options = Options.Create(new JwtOptions
        {
            Secret = "abcdefghijklmnopqrstuvwxyz123456",
            ExpirationMinutes = 15
        });
        var service = new JwtTokenService(options);

        var token = service.CreateAccessToken(userId, tokenVersion, out _);
        var payload = ReadJwtPayload(token);

        Assert.Equal(userId.ToString("D"), payload.GetProperty("sub").GetString());
        Assert.Equal(tokenVersion, payload.GetProperty("tv").GetInt32());
        Assert.True(payload.GetProperty("exp").GetInt64() > 0);
        Assert.True(payload.GetProperty("iat").GetInt64() > 0);
    }

    private static JsonElement ReadJwtPayload(string token)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        var jsonBytes = Base64UrlDecode(parts[1]);
        using var doc = JsonDocument.Parse(jsonBytes);
        return doc.RootElement.Clone();
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        var pad = 4 - (base64.Length % 4);
        if (pad is > 0 and < 4)
            base64 += new string('=', pad);

        return Convert.FromBase64String(base64);
    }
}
