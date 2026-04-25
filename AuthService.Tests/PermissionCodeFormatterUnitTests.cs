using AuthService.Auth;
using Xunit;

namespace AuthService.Tests;

public class PermissionCodeFormatterUnitTests
{
    [Fact]
    public void Format_WithUppercaseTypeCode_ReturnsPascalCaseCode()
    {
        var code = PermissionCodeFormatter.Format("Users", "READ");

        Assert.Equal("perm:Users.Read", code);
    }

    [Fact]
    public void Format_WithLowercaseTypeCode_ReturnsPascalCaseCode()
    {
        var code = PermissionCodeFormatter.Format("Users", "read");

        Assert.Equal("perm:Users.Read", code);
    }

    [Fact]
    public void Format_WithMixedCaseTypeCode_ReturnsPascalCaseCode()
    {
        var code = PermissionCodeFormatter.Format("SystemsRoutes", "CrEaTe");

        Assert.Equal("perm:SystemsRoutes.Create", code);
    }

    [Fact]
    public void Format_WithNullResource_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => PermissionCodeFormatter.Format(null!, "read"));

        Assert.Equal("resourcePascal", ex.ParamName);
    }

    [Fact]
    public void Format_WithEmptyResource_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => PermissionCodeFormatter.Format(string.Empty, "read"));

        Assert.Equal("resourcePascal", ex.ParamName);
    }

    [Fact]
    public void Format_WithEmptyTypeCode_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => PermissionCodeFormatter.Format("Users", string.Empty));

        Assert.Equal("typeCode", ex.ParamName);
    }

    [Fact]
    public void Format_WithWhitespaceTypeCode_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => PermissionCodeFormatter.Format("Users", "  "));

        Assert.Equal("typeCode", ex.ParamName);
    }
}
