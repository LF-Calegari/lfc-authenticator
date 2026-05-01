using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AuthService.Controllers.Common;

/// <summary>
/// Helpers reutilizáveis para validação de query string de listagens paginadas
/// (<c>page</c>, <c>pageSize</c>, filtros opcionais por <c>systemId</c>) e para
/// escapar entradas usadas em padrões <c>ILIKE</c>. Centraliza a aplicação dos
/// limites canônicos de paginação para que os controllers permaneçam declarativos.
/// </summary>
internal static class PagingQueryHelper
{
    /// <summary>Adiciona em <paramref name="modelState"/> os erros padronizados para <c>page</c>/<c>pageSize</c>.</summary>
    public static void ValidatePaging(ModelStateDictionary modelState, int page, int pageSize, int maxPageSize)
    {
        if (page <= 0)
            modelState.AddModelError(nameof(page), "page deve ser maior ou igual a 1.");

        if (pageSize <= 0 || pageSize > maxPageSize)
            modelState.AddModelError(nameof(pageSize), $"pageSize deve estar entre 1 e {maxPageSize}.");
    }

    /// <summary>Valida que <paramref name="systemId"/>, quando informado, não seja <see cref="Guid.Empty"/>.</summary>
    public static void ValidateOptionalSystemId(ModelStateDictionary modelState, Guid? systemId, string fieldName = "systemId")
    {
        if (systemId.HasValue && systemId.Value == Guid.Empty)
            modelState.AddModelError(fieldName, "systemId inválido.");
    }

    /// <summary>
    /// Escapa caracteres curinga (<c>%</c>, <c>_</c>) e o caractere de escape (<c>\</c>) na entrada do usuário
    /// para evitar que sejam interpretados como wildcards no <c>ILIKE</c>. Mantém o termo como busca literal parcial.
    /// </summary>
    public static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    /// <summary>
    /// Valida o trio canônico <c>Name</c>/<c>Code</c>/<c>Description</c> nas mesmas regras usadas
    /// por todos os controllers do catálogo (não-vazio após trim, max length consistente). Adiciona
    /// erros em <paramref name="modelState"/> usando os nomes de propriedade dos requests.
    /// </summary>
    public static void ValidateNameCodeDescription(
        ModelStateDictionary modelState,
        string name,
        string code,
        string? descriptionOrNull,
        string nameField = "Name",
        string codeField = "Code",
        string descriptionField = "Description",
        int nameMax = 80,
        int codeMax = 50,
        int descriptionMax = 500)
    {
        if (string.IsNullOrWhiteSpace(name))
            modelState.AddModelError(nameField, $"{nameField} é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(code))
            modelState.AddModelError(codeField, $"{codeField} é obrigatório e não pode ser apenas espaços.");

        if (name.Length > nameMax)
            modelState.AddModelError(nameField, $"{nameField} deve ter no máximo {nameMax} caracteres.");

        if (code.Length > codeMax)
            modelState.AddModelError(codeField, $"{codeField} deve ter no máximo {codeMax} caracteres.");

        if (descriptionOrNull is not null && descriptionOrNull.Length > descriptionMax)
            modelState.AddModelError(descriptionField, $"{descriptionField} deve ter no máximo {descriptionMax} caracteres.");
    }
}
