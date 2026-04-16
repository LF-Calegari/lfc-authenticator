using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthService.Helpers;

internal static class DbExceptionHelper
{
    private const string UniqueViolationSqlState = "23505";
    private const string ForeignKeyViolationSqlState = "23503";

    internal static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is PostgresException pg && pg.SqlState == UniqueViolationSqlState)
                return true;
        }

        var text = string.Join(" ", GetExceptionMessages(ex));
        return text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
               || text.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
               || text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is PostgresException pg && pg.SqlState == ForeignKeyViolationSqlState)
                return true;
        }

        return false;
    }

    private static IEnumerable<string> GetExceptionMessages(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            yield return e.Message;
    }
}
