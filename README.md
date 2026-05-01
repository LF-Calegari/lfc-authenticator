# Auth Service (lfc-authenticator)

API REST em **ASP.NET Core** para **autenticação JWT**, **autorização baseada em permissões** persistidas em **PostgreSQL** e **cadastros** correlatos (sistemas, rotas, tipos de permissão, permissões, papéis e vínculos). O serviço centraliza o catálogo oficial de permissões e padroniza o que cada usuário pode fazer nos demais sistemas do ecossistema.

> Atualização de contrato (issue #36): as rotas oficiais públicas são prefixadas por `/api/v1`.

---

## Índice

- [Visão geral](#visão-geral)
- [Requisitos](#requisitos)
- [Início rápido](#início-rápido)
- [Configuração e variáveis de ambiente](#configuração-e-variáveis-de-ambiente)
- [Fluxo de inicialização](#fluxo-de-inicialização-da-aplicação)
- [Arquitetura em alto nível](#arquitetura-em-alto-nível)
- [Autenticação e autorização](#autenticação-e-autorização)
- [Versionamento da API (`/api/v1`)](#versionamento-da-api-v1)
- [Documentação OpenAPI (Swagger)](#documentação-openapi-swagger)
- [Referência de rotas](#referência-de-rotas)
- [Exemplos de uso](#exemplos-de-uso)
- [Convenções de erro e códigos HTTP](#convenções-de-erro-e-códigos-http)
- [Docker Compose](#docker-compose)
- [Migrations (EF Core)](#migrations-ef-core)
- [Testes automatizados](#testes-automatizados)
- [Troubleshooting](#troubleshooting)
- [Boas práticas e segurança](#boas-práticas-e-segurança)
- [Contribuindo e próximos passos](#contribuindo-e-próximos-passos)
- [Apêndice: SDK .NET em container](#apêndice-sdk-net-em-container)

---

## Visão geral

### Contexto e objetivo

O Auth Service expõe endpoints para:

- Emitir e validar **tokens JWT** vinculados a usuários.
- Resolver **autorização** comparando permissões efetivas do usuário (diretas e via papéis) com políticas declaradas nos controllers.
- Manter cadastros com **exclusão lógica (*soft delete*)** e rotas de **restauração** onde aplicável.

### Domínio (produto) — modelo alvo

Cadastros conceituais:

1. **Cliente** — entidade de negócio (PF/PJ), com contatos extras e relação com usuários.
2. **Sistema** — agrupa recursos lógicos.
3. **Recurso** — agrupamento dentro de um sistema (evolução futura).
4. **Rota** — endpoints ou caminhos (hoje modeladas como `systems/routes` ligadas ao sistema).
5. **Permissão** — ações sobre recursos/rotas.

Ações padrão de permissão: `create`, `read`, `update`, `delete`, `restore`.

### O que está implementado hoje

- JWT com validação de **versão de sessão** (`tokenVersion` no banco + *claim* `tv` no token); **logout** invalida tokens anteriores.
- Políticas `Authorize` no formato `perm:<Recurso>.<Ação>` (ex.: `perm:Systems.Read`), resolvidas para **GUIDs** de permissão no banco.
- **Catálogo oficial** do sistema **authenticator** (sistemas, rotas, tipos de permissão e linhas de permissão) criado de forma **idempotente** na subida em **Development** e **Production** (não roda no host em ambiente **Testing**; nos testes, o *factory* aplica o mesmo seed após as migrations).
- CRUD com *soft delete* e `POST`/`PATCH` de restauração nos recursos documentados na [referência de rotas](#referência-de-rotas).

---

## Requisitos

| Item | Versão / notas |
|------|----------------|
| SDK .NET | **10.x** (alvo `net10.0`) |
| Banco | **PostgreSQL** (local, Docker ou remoto; desenvolvimento testado com **18** no Compose) |
| Docker (opcional) | Docker Engine + Compose v2; **rede externa obrigatória** em ambiente integrado, **no máximo 30 IPs** úteis na sub-rede (ex.: `/27`); **mesma rede** que os demais serviços do ecossistema (padrão `lfc_platform_network`). |
| Ferramenta EF (migrations) | `dotnet-ef` (global ou via `dotnet tool restore` no projeto) |

---

## Início rápido

### Opção A — Docker Compose (recomendado para ambiente integrado)

1. **Rede Docker:** crie a rede externa **uma vez** (a mesma usada pelos outros serviços do ecossistema) — veja [Docker Compose — Rede externa](#rede-externa).
2. Na **raiz** do repositório: `cp .env.example .env` e ajuste `POSTGRES_PASSWORD` (use uma senha forte em produção).
3. Subir API + PostgreSQL: `docker compose up -d --build`
4. Aplicar migrations (primeira vez ou após alteração do modelo):  
   `docker compose --profile migrate run --rm migrate`
5. API no host: **https://localhost:8080** (mapeamento `8080:5042`; Kestrel usa certificado de desenvolvimento gerado na imagem Docker — o navegador pode alertar até você confiar no certificado ou usar `-k` no `curl`).

### Opção B — `dotnet run` no host

1. PostgreSQL acessível (ex.: `localhost:5432` se usar o container só do `db`).
2. Ajuste `ConnectionStrings:DefaultConnection` em `AuthService/appsettings.Development.json` ou sobrescreva via variável de ambiente (veja tabela abaixo). A senha deve ser a **mesma** configurada no PostgreSQL (ex.: a do `.env` se o banco for o do Compose).
3. Aplicar migrations no banco alvo (veja [Migrations](#migrations-ef-core)).
4. Na pasta `AuthService`: `dotnet run`  
   URLs padrão do perfil HTTP: **http://localhost:5052** (veja `Properties/launchSettings.json`).

Todas as rotas da API REST ficam sob o prefixo **`/api/v1`** (ex.: `GET http://localhost:5052/api/v1/health`).

---

## Configuração e variáveis de ambiente

| Origem | Descrição |
|--------|-----------|
| `ConnectionStrings:DefaultConnection` | Connection string **Npgsql** com banco (ex.: `Database=AuthenticatorDb`). No Compose: `ConnectionStrings__DefaultConnection`. |
| `ASPNETCORE_ENVIRONMENT` | `Development`, `Production` ou `Testing`. Em **Testing**, não há redirecionamento HTTPS e o seed do catálogo no `Program` é omitido (testes fazem seed no *factory*). |
| `Auth:Jwt:Secret` | Segredo HMAC do JWT; **mínimo 32 caracteres**. Em produção, use segredo forte e armazenamento seguro — **não** commite valores reais. |
| `Auth:Jwt:ExpirationMinutes` | Validade do access token em minutos. |
| `DEFAULT_SYSTEM_USER_PASSWORD` | Credencial do usuário `root@email.com.br`. **Obrigatória** em Development e Production; fail-fast se ausente. No Docker Compose o default é `toor`. |
| `AUTH_SERVICE_TEST_PG_BASE` | Obrigatória para **testes de integração**: connection string **sem** `Database` (o teste cria um banco dedicado por execução). |

Exemplo de override no shell (Linux):

```bash
export ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=AuthenticatorDb;Username=auth;Password=SuaSenha"
export Auth__Jwt__Secret="sua-chave-com-pelo-menos-32-caracteres!!"
```

---

## Fluxo de inicialização da aplicação

1. **`WebApplication.CreateBuilder`** — registra `AppDbContext` (PostgreSQL via **Npgsql** / `UseNpgsql`), opções JWT, serviços de autenticação/autorização customizados, controllers e Swagger.
2. **Pipeline HTTP** — em ambientes diferentes de **Testing**, `UseHttpsRedirection`. **Swagger** e **Swagger UI** são registrados **antes** de autenticação/autorização, ficando **anônimos**.
3. **`UseAuthentication`** / **`UseAuthorization`** — JWT *handler* valida cabeçalho `Authorization: Bearer …`, *claims* e coerência com o usuário no banco (`TokenVersion`, ativo).
4. **`MapGroup("/api/v1").MapControllers()`** — todas as rotas de API ficam versionadas em `/api/v1`.
5. **Pós-build (Development e Production apenas)** — sequência idempotente de seeders, executada nessa ordem:
   1. `SystemSeeder.EnsureSystemsAsync` garante o sistema `authenticator`.
   2. `AuthenticatorRoutesSeeder.EnsureRoutesAsync` cadastra o catálogo de rotas autenticadas do authenticator (uma linha por endpoint REST exposto, com `Code` no padrão `AUTH_V1_<RECURSO>_<AÇÃO>`).
   3. `PermissionTypeSeeder.EnsurePermissionTypesAsync` garante os 5 tipos canônicos (`create`, `read`, `update`, `delete`, `restore`).
   4. `AuthenticatorPermissionsSeeder.EnsurePermissionsAsync` cria uma linha de `Permissions` por rota do authenticator, ligando-a ao tipo natural inferido pelo sufixo do `Code` da rota.
   5. `RootUserSeeder.EnsureRootUserAsync` garante o usuário `root@email.com.br`, seu cliente vinculado e a role `root`. A credencial vem de `DEFAULT_SYSTEM_USER_PASSWORD` (fail-fast se ausente); no banco persiste-se **somente o hash** PBKDF2.
   6. `RootRolePermissionsSeeder.EnsureRootRolePermissionsAsync` vincula a role `root` a **todas** as permissões do sistema authenticator.

   **Em produção, defina uma senha forte para o root e troque imediatamente após o primeiro acesso.** Em ambiente `Testing` o pipeline de seeders é executado pelo `WebApplicationFactory` (não pelo `Program.cs`).

---

## Arquitetura em alto nível

```
AuthService/
├── Controllers/          # Endpoints REST por agregado (Auth, Systems, Users, …)
├── Auth/                 # JWT, handler Bearer, políticas perm:*, permissões efetivas
├── Security/             # Hash e verificação de senha (ASP.NET Identity PasswordHasher / PBKDF2)
├── Data/                 # AppDbContext, migrations, seeders (catálogo oficial e usuários base)
├── Models/               # Entidades EF Core
├── OpenApi/              # Filtros Swagger (prefixo /api/v1 nos paths do documento)
└── Program.cs            # Composição, pipeline, mapa de rotas
```

- **Camada de API:** controllers ASP.NET Core, validação via *DataAnnotations* e `ModelState`.
- **Camada de aplicação/infra:** `Auth` + `Data` concentram regras de token, resolução de políticas e persistência.
- **Autorização:** `FallbackPolicy` exige usuário autenticado (JWT), exceto onde há `[AllowAnonymous]`.

---

## Autenticação e autorização

### JWT (Bearer)

- **Login:** `POST /api/v1/auth/login` com `email` e `password` → resposta com `token`.
- **Cabeçalho:** `Authorization: Bearer <jwt>`.
- O token inclui identificação do usuário (`sub`) e versão de sessão (`tv`). Alterações em `TokenVersion` (ex.: **logout**) invalidam tokens antigos.

### Senhas (armazenamento e boas práticas)

- A coluna `Users.Password` guarda **apenas hash** (PBKDF2 via `PasswordHasher` do ASP.NET Core Identity), nunca a senha em texto puro.
- `POST /api/v1/users` e `PUT /api/v1/users/{id}/password` aceitam a senha em texto na requisição; a API calcula o hash antes de persistir.
- O login compara a senha informada com o hash (`UserPasswordHasher` em `Security/`). Bases com usuários legados em texto plano: no primeiro login bem-sucedido (ou na *seed* idempotente, quando aplicável), o valor é substituído por hash.
- Boas práticas: senhas fortes, rotação após primeiro acesso do usuário padrão, e segredo JWT forte em produção (ver [Configuração e variáveis de ambiente](#configuração-e-variáveis-de-ambiente)).

### Políticas `perm:<Chave>`

- No código, constantes como `PermissionPolicies.SystemsRead` correspondem à política **`perm:Systems.Read`**.
- O *handler* de autorização resolve a chave (`Systems` + `Read`) para um **GUID** de permissão no banco (via sistema e tipo de permissão do catálogo) e verifica se esse id está no conjunto **efetivo** do usuário.
- **Permissões efetivas** = permissões ligadas diretamente ao usuário **ou** alcançadas por **papéis** (`UserRoles` → `RolePermissions`).

### Endpoints somente autenticados (sem política `perm:`)

- `GET /api/v1/auth/verify-token`
- `GET /api/v1/auth/permissions`
- `GET /api/v1/auth/logout`

### Anonimato permitido

- `GET /api/v1/health`
- `POST /api/v1/auth/login`
- Documentação Swagger (`/docs`, JSON em `/swagger/v1/swagger.json`)

---

## Versionamento da API (`/api/v1`)

Todas as rotas listadas na referência abaixo são relativas ao prefixo **`/api/v1`**. Exemplo completo: `https://localhost:7218/api/v1/systems`.

---

## Documentação OpenAPI (Swagger)

| Recurso | Caminho |
|---------|---------|
| Swagger UI | **`/docs`** |
| OpenAPI JSON | **`/swagger/v1/swagger.json`** (paths já prefixados com `/api/v1` via filtro de documento) |

A UI está configurada para não exigir autenticação; para testar endpoints protegidos, use **Authorize** na Swagger UI e informe `Bearer <token>`.

---

## Referência de rotas

Legenda:

- **Auth:** `Não` = anônimo; `Sim` = JWT obrigatório.
- **Permissão:** política `perm:…` exigida; `—` = somente autenticação.

### Saúde

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `GET` | `/api/v1/health` | Não | — |

### Autenticação — `/api/v1/auth`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/auth/login` | Não | — |
| `GET` | `/api/v1/auth/verify-token` | Sim | — |
| `GET` | `/api/v1/auth/permissions` | Sim | — |
| `GET` | `/api/v1/auth/logout` | Sim | — |

**`POST /api/v1/auth/login`** — body: `email`, `password`, `systemId` (todos obrigatórios). Resposta: `{ token }`.

**`GET /api/v1/auth/verify-token`** — headers: `Authorization: Bearer <jwt>`, `X-System-Id: <systemId>`, `X-Route-Code: <codeDaRota>`. Resposta enxuta: `{ valid, issuedAt, expiresAt }`. Devolve 403 quando a rota informada não está no catálogo do usuário.

**`GET /api/v1/auth/permissions`** — headers: `Authorization: Bearer <jwt>`, `X-System-Id: <systemId>`. Resposta: `{ user: { id, name, email, identity }, routes: [<routeCode>, ...] }` listando o catálogo de rotas seedadas do sistema do header.

**`GET /api/v1/auth/logout`** — headers: `Authorization: Bearer <jwt>`. Incrementa `TokenVersion` e invalida tokens anteriores.

### Sistemas — `/api/v1/systems`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/systems` | Sim | `perm:Systems.Create` |
| `GET` | `/api/v1/systems` | Sim | `perm:Systems.Read` |
| `GET` | `/api/v1/systems/{id}` | Sim | `perm:Systems.Read` |
| `PUT` | `/api/v1/systems/{id}` | Sim | `perm:Systems.Update` |
| `DELETE` | `/api/v1/systems/{id}` | Sim | `perm:Systems.Delete` |
| `POST` | `/api/v1/systems/{id}/restore` | Sim | `perm:Systems.Restore` |

Corpo típico de criação/atualização: `name`, `code`, `description` (opcional). Registros *soft-deleted* retornam **404** em leitura/alteração/exclusão até restaurados.

### Rotas de sistema — `/api/v1/systems/routes`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/systems/routes` | Sim | `perm:SystemsRoutes.Create` |
| `GET` | `/api/v1/systems/routes` | Sim | `perm:SystemsRoutes.Read` |
| `GET` | `/api/v1/systems/routes/{id}` | Sim | `perm:SystemsRoutes.Read` |
| `PUT` | `/api/v1/systems/routes/{id}` | Sim | `perm:SystemsRoutes.Update` |
| `DELETE` | `/api/v1/systems/routes/{id}` | Sim | `perm:SystemsRoutes.Delete` (¹) |
| `POST` | `/api/v1/systems/routes/{id}/restore` | Sim | `perm:SystemsRoutes.Restore` |
| `POST` | `/api/v1/systems/routes/sync` | Sim | `perm:SystemsRoutes.Update` |

`systemId` obrigatório; `code` único globalmente. Listagens consideram sistema pai ativo.

(¹) **`DELETE /api/v1/systems/routes/{id}`** retorna **409 Conflict** quando a rota tem `Permissions` ativas (`DeletedAt IS NULL`) vinculadas. Payload: `{ "message": "Não é possível excluir a rota: existem permissões ativas vinculadas. Remova as permissões antes.", "linkedPermissionsCount": N }`. No caminho 409, nenhum side-effect é persistido (rota e permissões permanecem inalteradas). Permissões soft-deletadas não bloqueiam o delete. Para forçar a remoção, exclua/soft-delete as Permissions vinculadas antes (ou use `POST /sync?prune=true` para cascade pelo catálogo).

**`GET /api/v1/systems/routes`** suporta filtro/busca/paginação via query string:

| Param | Default | Notas |
|-------|---------|-------|
| `systemId` | _ausente_ | Quando informado, restringe à `SystemId` indicada. `Guid.Empty` retorna **400**; `systemId` válido apontando para sistema inexistente retorna **200** com `data: []`. |
| `q` | `""` | Busca case-insensitive (ILIKE) com matching parcial em `Code` **e** `Name` (OR). Caracteres `%`, `_` e `\` são escapados (literais). |
| `page` | `1` | 1-based. `<= 0` retorna **400**. |
| `pageSize` | `20` | Máximo `100`. `<= 0` ou `> 100` retornam **400**. |
| `includeDeleted` | `false` | Quando `true`, inclui rotas soft-deletadas **e** rotas cujo sistema pai foi soft-deletado (cenário admin). Default mantém o filtro `ActiveRoutesWithActiveSystem`. |

Resposta paginada: `{ data, page, pageSize, total }`. `total` reflete o total após filtros, antes de `Skip/Take`. Ordenação determinística por `Code` ascendente, com `Id` como desempate. Página além do total retorna **200** com `data: []`.

**Política JWT alvo (`systemTokenTypeId`)** — toda rota referencia um `SystemTokenType` (`/api/v1/tokens/types`) ativo via FK NOT NULL. O campo é **obrigatório** em `POST` e `PUT`; payloads sem ele retornam **400** com erro em `ModelState["SystemTokenTypeId"]`. `Guid.Empty`, IDs inexistentes ou referenciando registros soft-deletados também retornam **400**. Restaurar uma rota cujo `SystemTokenType` foi removido depois do soft-delete também retorna **400** (mesmo padrão da validação de sistema inativo).

Existe um catálogo canônico garantido pelo `SystemTokenTypeSeeder` na inicialização do serviço, com pelo menos `Code='default'` (`Name='Default'`). Esse é o code usado como fallback no `sync` quando o item não especifica um `systemTokenTypeCode`.

`GET /api/v1/systems/routes` e `GET /api/v1/systems/routes/{id}` retornam, além dos campos da rota, três campos denormalizados via Join: `systemTokenTypeId`, `systemTokenTypeCode`, `systemTokenTypeName`. Os campos `code` e `name` do token type **não** entram no body de `POST`/`PUT` — são apenas leitura.

**`POST /api/v1/systems/routes/sync`** — auto-registro do catálogo de rotas por um sistema-cliente. Body:

```json
{
  "systemCode": "kurtto",
  "routes": [
    { "code": "KURTTO_V1_X_LIST", "name": "GET /api/v1/x", "description": "...", "permissionTypeCode": "read", "systemTokenTypeCode": "default" }
  ]
}
```

Query string: `?prune=false` (padrão). Quando `prune=true`, rotas do sistema que **sumirem** do payload são soft-deletadas junto com suas Permissions vinculadas. Quando `permissionTypeCode` é informado, a `Permission(Route, Type)` correspondente é criada/reativada automaticamente. `systemTokenTypeCode` é **opcional** — quando omitido, o sync usa `default`. Resposta: `{ created, updated, reactivated, deleted }`. Erros: 404 (`systemCode` desconhecido), 400 (`permissionTypeCode` desconhecido, `systemTokenTypeCode` desconhecido — listando os codes inválidos — ou `code` duplicado no payload), 409 (`code` já em uso por outro sistema — `UX_Routes_Code` é unique global).

### Usuários — `/api/v1/users`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/users` | Sim | `perm:Users.Create` |
| `GET` | `/api/v1/users` | Sim | `perm:Users.Read` |
| `GET` | `/api/v1/users/{id}` | Sim | `perm:Users.Read` |
| `PUT` | `/api/v1/users/{id}` | Sim | `perm:Users.Update` |
| `PUT` | `/api/v1/users/{id}/password` | Sim | `perm:Users.Update` |
| `DELETE` | `/api/v1/users/{id}` | Sim | `perm:Users.Delete` |
| `POST` | `/api/v1/users/{id}/restore` | Sim | `perm:Users.Restore` |
| `POST` | `/api/v1/users/{id}/force-logout` | Sim | `perm:Users.Update` |
| `POST` | `/api/v1/users/{id}/permissions` | Sim | `perm:Users.Update` |
| `DELETE` | `/api/v1/users/{id}/permissions/{permissionId}` | Sim | `perm:Users.Update` |
| `POST` | `/api/v1/users/{id}/roles` | Sim | `perm:Users.Update` |
| `DELETE` | `/api/v1/users/{id}/roles/{roleId}` | Sim | `perm:Users.Update` |

**POST:** `name`, `email`, `password`, `identity`, `active` (opcional, padrão `true`). **PUT** usuário: `name`, `email`, `identity`, `active` (sem senha). Email normalizado (ex.: minúsculas).

**GET** `/api/v1/users/{id}` retorna também os vínculos ativos **`roles`** (lista com `id` inteiro, `userId`, `roleId`, auditoria e `deletedAt`) e **`permissions`** (lista com `id` GUID, `userId`, `permissionId`, auditoria e `deletedAt`). Listagens **`GET /api/v1/users`** e respostas de criação/atualização devolvem `roles` e `permissions` como arrays vazios.

**`POST /api/v1/users/{id}/force-logout`** — admin invalida todas as sessões ativas do usuário-alvo incrementando o `TokenVersion`. Mesmo mecanismo do `GET /auth/logout`, porém aplicado a um terceiro. Caller precisa de `perm:Users.Update`. O `JwtBearerHandler` valida o claim `tv` contra o banco a cada request, então tokens emitidos antes da chamada passam a falhar com 401 automaticamente. Self-target (`id` igual ao caller do JWT) retorna 400 e orienta a usar `/auth/logout`. Usuário inexistente ou soft-deletado retorna 404. Resposta `200 OK`:

```json
{
  "message": "Sessões do usuário invalidadas com sucesso.",
  "userId": "<guid>",
  "newTokenVersion": 1
}
```

A operação é idempotente em re-execução: cada chamada incrementa o contador (sessões emitidas entre as chamadas continuam válidas até o próximo incremento) e gera log estruturado `Information` `ForceLogout: target {UserId}, by {CallerId}, newTokenVersion={N}` para auditoria.

### Clientes — `/api/v1/clients`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/clients` | Sim | `perm:Clients.Create` |
| `GET` | `/api/v1/clients` | Sim | `perm:Clients.Read` |
| `GET` | `/api/v1/clients/{id}` | Sim | `perm:Clients.Read` |
| `PUT` | `/api/v1/clients/{id}` | Sim | `perm:Clients.Update` |
| `DELETE` | `/api/v1/clients/{id}` | Sim | `perm:Clients.Delete` |
| `POST` | `/api/v1/clients/{id}/restore` | Sim | `perm:Clients.Restore` |
| `POST` | `/api/v1/clients/{id}/emails` | Sim | `perm:Clients.Update` |
| `DELETE` | `/api/v1/clients/{id}/emails/{emailId}` | Sim | `perm:Clients.Update` |
| `POST` | `/api/v1/clients/{id}/mobiles` | Sim | `perm:Clients.Update` |
| `DELETE` | `/api/v1/clients/{id}/mobiles/{phoneId}` | Sim | `perm:Clients.Update` |
| `POST` | `/api/v1/clients/{id}/phones` | Sim | `perm:Clients.Update` |
| `DELETE` | `/api/v1/clients/{id}/phones/{phoneId}` | Sim | `perm:Clients.Update` |

Regras de negócio principais: `type` imutável (`PF`/`PJ`), validação de CPF/CNPJ por tipo, unicidade global de CPF/CNPJ, máximo de 3 emails extras, 3 celulares e 3 telefones por cliente, bloqueio para remoção de email extra que esteja sendo usado como username.

### Tipos de token — `/api/v1/tokens/types`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/tokens/types` | Sim | `perm:SystemTokensTypes.Create` |
| `GET` | `/api/v1/tokens/types` | Sim | `perm:SystemTokensTypes.Read` |
| `GET` | `/api/v1/tokens/types/{id}` | Sim | `perm:SystemTokensTypes.Read` |
| `PUT` | `/api/v1/tokens/types/{id}` | Sim | `perm:SystemTokensTypes.Update` |
| `DELETE` | `/api/v1/tokens/types/{id}` | Sim | `perm:SystemTokensTypes.Delete` |
| `POST` | `/api/v1/tokens/types/{id}/restore` | Sim | `perm:SystemTokensTypes.Restore` |

### Tipos de permissão — `/api/v1/permissions/types`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/permissions/types` | Sim | `perm:PermissionsTypes.Create` |
| `GET` | `/api/v1/permissions/types` | Sim | `perm:PermissionsTypes.Read` |
| `GET` | `/api/v1/permissions/types/{id}` | Sim | `perm:PermissionsTypes.Read` |
| `PUT` | `/api/v1/permissions/types/{id}` | Sim | `perm:PermissionsTypes.Update` |
| `DELETE` | `/api/v1/permissions/types/{id}` | Sim | `perm:PermissionsTypes.Delete` |
| `POST` | `/api/v1/permissions/types/{id}/restore` | Sim | `perm:PermissionsTypes.Restore` |

`code` único globalmente.

### Papéis (roles) — `/api/v1/roles`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/roles` | Sim | `perm:Roles.Create` |
| `GET` | `/api/v1/roles` | Sim | `perm:Roles.Read` |
| `GET` | `/api/v1/roles/{id}` | Sim | `perm:Roles.Read` |
| `PUT` | `/api/v1/roles/{id}` | Sim | `perm:Roles.Update` |
| `DELETE` | `/api/v1/roles/{id}` | Sim | `perm:Roles.Delete` |
| `POST` | `/api/v1/roles/{id}/restore` | Sim | `perm:Roles.Restore` |
| `POST` | `/api/v1/roles/{id}/permissions` | Sim | `perm:Roles.Update` |
| `DELETE` | `/api/v1/roles/{id}/permissions/{permissionId}` | Sim | `perm:Roles.Update` |

### Permissões — `/api/v1/permissions`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/permissions` | Sim | `perm:Permissions.Create` |
| `GET` | `/api/v1/permissions` | Sim | `perm:Permissions.Read` |
| `GET` | `/api/v1/permissions/{id}` | Sim | `perm:Permissions.Read` |
| `PUT` | `/api/v1/permissions/{id}` | Sim | `perm:Permissions.Update` |
| `DELETE` | `/api/v1/permissions/{id}` | Sim | `perm:Permissions.Delete` |
| `POST` | `/api/v1/permissions/{id}/restore` | Sim | `perm:Permissions.Restore` |

Corpo: `routeId`, `permissionTypeId`, `description` (opcional). Restauração exige referências ativas coerentes com as regras do controller.

---

## Exemplos de uso

Substitua a base pela URL da sua instância (`http://localhost:5052`, `https://localhost:8080` no Compose, etc.).

**Login**

```bash
curl -s -X POST "http://localhost:5052/api/v1/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"usuario@exemplo.com","password":"suaSenha"}'
```

Resposta esperada (200): JSON com `token`.

**Chamada autenticada (ex.: listar sistemas)**

```bash
curl -s "http://localhost:5052/api/v1/systems" \
  -H "Authorization: Bearer SEU_JWT_AQUI"
```

**Verificar validade do token e autorização para uma rota**

```bash
curl -s "http://localhost:5052/api/v1/auth/verify-token" \
  -H "Authorization: Bearer SEU_JWT_AQUI" \
  -H "X-System-Id: 11111111-1111-1111-1111-111111111111" \
  -H "X-Route-Code: AUTH_V1_USERS_LIST"
```

Resposta (200): `{ valid, issuedAt, expiresAt }`. Devolve **403** se o usuário não tem a rota informada autorizada e **400** se a rota não existe no sistema do header.

**Catálogo de rotas do sistema do usuário**

```bash
curl -s "http://localhost:5052/api/v1/auth/permissions" \
  -H "Authorization: Bearer SEU_JWT_AQUI" \
  -H "X-System-Id: 11111111-1111-1111-1111-111111111111"
```

Resposta (200): `{ user: { id, name, email, identity }, routes: [<routeCode>, ...] }`.

**Health check**

```bash
curl -s "http://localhost:5052/api/v1/health"
```

---

## Convenções de erro e códigos HTTP

| Código | Uso típico |
|--------|------------|
| **200 OK** | Leitura ou escrita com corpo de sucesso. |
| **201 Created** | Criação bem-sucedida (ex.: `POST` com `CreatedAtAction`). |
| **204 No Content** | Exclusão lógica bem-sucedida (`DELETE`). |
| **400 Bad Request** | Validação de entrada (`ModelState` / `ValidationProblem`), regras de negócio ou estado inválido; corpo frequentemente JSON (`message` ou detalhes de campo). |
| **401 Unauthorized** | Credenciais inválidas no login; token ausente, inválido, expirado, revogado ou usuário inativo. |
| **403 Forbidden** | JWT válido mas **sem** permissão para a política `perm:…` exigida. |
| **404 Not Found** | Recurso inexistente ou *soft-deleted* em operações que exigem entidade ativa. |
| **409 Conflict** | Violação de unicidade (email, `code`, par já vinculado, etc.). |

Mensagens de autenticação e de regra de negócio costumam vir como JSON com propriedade `message`; erros de validação seguem o padrão de **problem details** do ASP.NET Core quando aplicável.

---

## Docker Compose

O arquivo **`docker-compose.yml`** está na **raiz** do repositório.

Este serviço faz parte de um **sistema maior**: em Docker, ele deve usar a **mesma rede externa** que os demais projetos para que os containers se comuniquem por nome de host interno.

### Rede externa

É **obrigatória** uma rede Docker externa com **no máximo 30 IPs** disponíveis para containers (exemplo de dimensionamento: sub-rede **`/27`**, com 30 endereços úteis).

Antes de subir os serviços, garanta que a rede externa exista:

```bash
docker network create \
  --driver bridge \
  --subnet 172.30.0.0/27 \
  lfc_platform_network
```

> A stack usa a rede externa `lfc_platform_network` por padrão. Se precisar usar outro nome, defina `EXTERNAL_NETWORK_NAME` no ambiente antes de executar o Compose.

| Serviço | Função |
|---------|--------|
| `db` | PostgreSQL **18** (`postgres:18-alpine`); porta **5432** no host; volume `pg_data`. |
| `app` | API com `dotnet watch`, volume `./AuthService:/app`, porta **8080→5042** (HTTPS no container). |
| `migrate` (profile `migrate`) | `dotnet ef database update` contra o `db`. |
| `test` (profile `test`) | Executa `dotnet test` com `AUTH_SERVICE_TEST_PG_BASE` apontando para `db`. |

**Subir stack:** `docker compose up -d --build`  
**Migrations:** `docker compose --profile migrate run --rm migrate`  
**Testes de integração:** `docker compose --profile test run --rm test`

> O profile `migrate` apaga `obj/` e `bin/` em `./AuthService` antes do `dotnet restore`, para não reutilizar um `project.assets.json` gerado noutro ambiente (ex.: Windows no host), o que pode causar `FileNotFoundException` ao carregar `Microsoft.EntityFrameworkCore.Design.dll` dentro do container Linux.

Com o `app` em execução, migrations manuais no container:

```bash
docker compose exec app sh -c "rm -rf obj bin && dotnet restore && dotnet tool restore && dotnet ef database update"
```

String típica **na rede do Compose** (servidor `db`):

`Host=db;Port=5432;Database=AuthenticatorDb;Username=auth;Password=<sua senha>`

---

## Migrations (EF Core)

### Histórico: SQL Server → PostgreSQL

As migrations antigas (provider SQL Server) foram substituídas por um **baseline único** para PostgreSQL (`InitialPostgreSQLBaseline`), gerado com `dotnet ef migrations add`. Ambientes que já tinham o schema no SQL Server devem tratar **cutover de dados** separadamente (export/import ou ETL); este repositório não mantém mais migrations incrementais para SQL Server.

Na pasta do projeto web (ou raiz com caminhos ajustados):

```bash
dotnet ef database update \
  --project AuthService/AuthService.csproj \
  --startup-project AuthService/AuthService.csproj
```

Para **adicionar** migration (apenas quando houver mudança de modelo):

```bash
dotnet ef migrations add NomeDescritivoDaMigration \
  --project AuthService/AuthService.csproj \
  --startup-project AuthService/AuthService.csproj \
  --output-dir Data/Migrations
```

---

## Testes automatizados

- Projeto: **`AuthService.Tests`**
- Há dois tipos de suíte:
  - **Unitária**: valida componentes isolados (sem banco/infra externa).
  - **Integração**: usa PostgreSQL real e sobe a aplicação completa em memória.
- Para executar apenas testes unitários:

```bash
dotnet test AuthService.Tests/AuthService.Tests.csproj --filter "FullyQualifiedName~UnitTests"
```

- Utilizam **PostgreSQL real**; cada execução cria um banco `auth_svc_it_<guid>`, aplica migrations, executa o pipeline de seeders (`SystemSeeder` → `AuthenticatorRoutesSeeder` → `PermissionTypeSeeder` → `AuthenticatorPermissionsSeeder` → `RootUserSeeder` → `RootRolePermissionsSeeder`) e remove o banco ao final.
- **Obrigatório** definir `AUTH_SERVICE_TEST_PG_BASE` **sem** `Database`:

```bash
export AUTH_SERVICE_TEST_PG_BASE="Host=127.0.0.1;Port=5432;Username=auth;Password=<MESMA_DO_.env>"
dotnet test AuthService.Tests/AuthService.Tests.csproj
```

Sem essa variável, o `WebApplicationFactory` falha na construção — comportamento esperado.

---

## Troubleshooting

| Sintoma | Verificação sugerida |
|---------|----------------------|
| Falha ao conectar ao PostgreSQL | Senha/porta/host; `pg_hba.conf` / rede; firewall. |
| `dotnet ef` não encontrado | `dotnet tool install --global dotnet-ef` ou `dotnet tool restore` no projeto. |
| 401 em todas as rotas protegidas | Cabeçalho `Authorization: Bearer`; relógio do cliente; token expirado ou após **logout**. |
| 403 em recurso específico | Usuário não possui GUID da permissão correspondente à política `perm:…` (vincular via papéis, permissões diretas no usuário ou processos de dados; consulte **`GET /api/v1/users/{id}`** para os vínculos). |
| 404 em entidade “que existe” | Pode estar *soft-deleted*; usar rota de **restore** quando aplicável. |
| Docker `app` não sobe | Aguardar healthcheck do `db`; conferir `POSTGRES_PASSWORD` no `.env`. |
| Caminho 404 na API | Prefixo **`/api/v1`** obrigatório em todos os controllers mapeados. |

---

## Boas práticas e segurança

- **Nunca** commitar segredos reais; use variáveis de ambiente ou cofres em produção.
- Troque `Auth:Jwt:Secret` em qualquer ambiente exposto; o repositório contém valores apenas para desenvolvimento.
- **HTTPS** em produção; em desenvolvimento local o perfil pode usar só HTTP.
- Após alterar rotas ou permissões, clientes podem chamar **`GET /api/v1/auth/permissions`** para obter o catálogo atualizado de `routes` do sistema; para validar acesso a uma rota concreta, use **`GET /api/v1/auth/verify-token`** com o header `X-Route-Code`.
- Mantenha o **README** alinhado às rotas ao introduzir novos controllers ou políticas.

---

## Contribuindo e próximos passos

1. Crie branch no padrão do time (ex.: `feature/<issue>/<descricao>`).
2. Alterações de modelo exigem **migration** gerada com `dotnet ef migrations add`.
3. Adicione ou atualize **testes de integração** para novos endpoints ou regras.
4. Adicione ou atualize **testes unitários** para regras críticas e serviços utilitários.
5. Atualize esta documentação (rotas, variáveis, troubleshooting) no mesmo PR quando o contrato público mudar.

**Roadmap alinhado ao domínio**

1. Entidade **Recurso** e relação explícita com rotas (hoje rotas ligam-se ao sistema).
2. Refinar matriz de permissões por recurso/rota e perfis.
3. Ampliar cobertura de testes (incluindo cenários explícitos de **403** por ausência de permissão).

---

## Apêndice: SDK .NET em container

Comandos úteis com a imagem `mcr.microsoft.com/dotnet/sdk:10.0` sem instalar o SDK no host.

### Novo projeto Web API

```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet new webapi --use-controllers -n MeuProjeto
```

### Pacotes Entity Framework (exemplos)

**PostgreSQL** (este repositório — provider **Npgsql**):

```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

**SQLite**

```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

**Design-time (migrations)**

```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet add package Microsoft.EntityFrameworkCore.Design
```

**CLI `dotnet-ef` (global na sessão do container)**

```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet tool install --global dotnet-ef
```
