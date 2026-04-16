using AuthService.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuthService.Tests;

public class DbExceptionHelperUnitTests
{
    [Theory]
    [InlineData("Violation of UNIQUE constraint")]
    [InlineData("unique constraint violation occurred")]
    [InlineData("Cannot insert duplicate key row")]
    public void IsUniqueConstraintViolation_WithMatchingMessage_ReturnsTrue(string message)
    {
        var inner = new Exception(message);
        var dbEx = new DbUpdateException("Save failed", inner);

        Assert.True(DbExceptionHelper.IsUniqueConstraintViolation(dbEx));
    }

    [Fact]
    public void IsUniqueConstraintViolation_WithUnrelatedMessage_ReturnsFalse()
    {
        var inner = new Exception("Some unrelated error");
        var dbEx = new DbUpdateException("Save failed", inner);

        Assert.False(DbExceptionHelper.IsUniqueConstraintViolation(dbEx));
    }

    [Fact]
    public void IsForeignKeyViolation_WithoutPostgresException_ReturnsFalse()
    {
        var inner = new Exception("FK error without PostgresException");
        var dbEx = new DbUpdateException("Save failed", inner);

        Assert.False(DbExceptionHelper.IsForeignKeyViolation(dbEx));
    }

}
