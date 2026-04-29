namespace AuthService.Controllers.Common;

/// <summary>
/// Envelope de resposta paginada usado por endpoints de listagem com busca/paginação server-side.
/// </summary>
/// <typeparam name="T">Tipo dos itens listados na página corrente.</typeparam>
/// <param name="Data">Itens da página corrente (após filtros e <c>Skip</c>/<c>Take</c>).</param>
/// <param name="Page">Número da página retornada (1-based) após defaults/validação.</param>
/// <param name="PageSize">Tamanho da página efetivamente aplicado após defaults/validação.</param>
/// <param name="Total">Total de registros que casam com os filtros aplicados, antes da paginação.</param>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Data,
    int Page,
    int PageSize,
    int Total);
