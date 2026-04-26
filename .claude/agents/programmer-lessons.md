# 🧠 Lições Aprendidas — Programmer

Erros que geraram BLOCKER em reviews anteriores. **Nunca repita esses padrões.**

> Este arquivo é atualizado automaticamente pelo programmer ao receber um BLOCKER do reviewer.
> Formato: `- [PR #XX] Descrição concisa do erro e como evitar`

---

<!-- Novas lições devem ser adicionadas abaixo desta linha -->
- [PR #146] Não ler `Request.Headers["X-Header-Name"]` direto em actions de controller (S6932) — usar binding via parâmetro `[FromHeader(Name = "X-Header-Name")] string? value` para que o ASP.NET Core trate parsing/validação e o OpenAPI gere o parâmetro automaticamente.
- [PR #146] Marcar como `static` métodos privados/internos de helpers de teste que não acessam estado de instância (CA1822) — evita warning Roslyn e deixa explícito que o helper é puro.
- [PR #149] Não deixar dispatcher tipo `if (path == X) { ... } if (path == Y) { ... }` crescer organicamente em `IOperationFilter.Apply` ou similar — Sonar `csharpsquid:S3776` (Cognitive Complexity) começa a reprovar acima de 15. Cada novo endpoint/branch a documentar deve virar um helper privado dedicado (`TryApply<Endpoint>Responses`) que retorna `bool` indicando "match e aplicado", deixando o `Apply` como dispatcher curto.