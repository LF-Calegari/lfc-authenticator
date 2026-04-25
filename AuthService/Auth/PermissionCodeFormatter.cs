using System.Globalization;

namespace AuthService.Auth;

/// <summary>
/// Formata codes legíveis de permissões no padrão <c>perm:&lt;Recurso&gt;.&lt;Acao&gt;</c>,
/// consistente com as constantes de <see cref="PermissionPolicies"/>.
/// </summary>
internal static class PermissionCodeFormatter
{
    /// <summary>
    /// Compõe a chave <c>perm:&lt;resourcePascal&gt;.&lt;PascalCase(typeCode)&gt;</c>.
    /// </summary>
    /// <param name="resourcePascal">Recurso lógico em PascalCase (ex.: <c>Users</c>, <c>SystemsRoutes</c>).</param>
    /// <param name="typeCode">Código do tipo de permissão em minúsculas (ex.: <c>read</c>, <c>create</c>).</param>
    public static string Format(string resourcePascal, string typeCode)
    {
        if (string.IsNullOrWhiteSpace(resourcePascal))
            throw new ArgumentException("Recurso é obrigatório.", nameof(resourcePascal));
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new ArgumentException("Tipo de permissão é obrigatório.", nameof(typeCode));

        return string.Concat("perm:", resourcePascal, ".", ToPascalCase(typeCode));
    }

    private static string ToPascalCase(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return trimmed;

        var lower = trimmed.ToLowerInvariant();
        var first = char.ToUpper(lower[0], CultureInfo.InvariantCulture);
        return lower.Length == 1 ? first.ToString() : string.Concat(first.ToString(), lower.AsSpan(1));
    }
}
