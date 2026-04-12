# Auth Service (lfc-authenticator)

API REST em **ASP.NET Core** para **autenticação JWT**, **autorização baseada em permissões** persistidas em **SQL Server** e **cadastros** correlatos (sistemas, rotas, tipos de permissão, permissões, papéis e vínculos). O serviço centraliza o catálogo oficial de permissões e padroniza o que cada usuário pode fazer nos demais sistemas do ecossistema.

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
- **Catálogo oficial** de sistemas, tipos de permissão e linhas de permissão criado de forma **idempotente** na subida em **Development** e **Production** (não roda no host em ambiente **Testing**; nos testes, o *factory* aplica o mesmo seed após as migrations), incluindo o sistema **Kurtto** e o seed de rotas credenciadas descrito em [Seed do sistema Kurtto](#seed-do-sistema-kurtto-lfc-kurtto).
- CRUD com *soft delete* e `POST`/`PATCH` de restauração nos recursos documentados na [referência de rotas](#referência-de-rotas).

---

## Requisitos

| Item | Versão / notas |
|------|----------------|
| SDK .NET | **10.x** (alvo `net10.0`) |
| Banco | **Microsoft SQL Server** (local, Docker ou remoto) |
| Docker (opcional) | Docker Engine + Compose v2; **rede externa obrigatória** em ambiente integrado, **no máximo 30 IPs** úteis na sub-rede (ex.: `/27`); **mesma rede** que os demais serviços do ecossistema (padrão `lfc_platform_network`). |
| Ferramenta EF (migrations) | `dotnet-ef` (global ou via `dotnet tool restore` no projeto) |

---

## Início rápido

### Opção A — Docker Compose (recomendado para ambiente integrado)

1. **Rede Docker:** crie a rede externa **uma vez** (a mesma usada pelos outros serviços do ecossistema, ex.: Kurtto) — veja [Docker Compose — Rede externa](#rede-externa).
2. Na **raiz** do repositório: `cp .env.example .env` e ajuste `MSSQL_SA_PASSWORD` (deve atender à política da Microsoft: maiúsculas, minúsculas, números e símbolos).
3. Subir API + SQL: `docker compose up -d --build`
4. Aplicar migrations (primeira vez ou após alteração do modelo):  
   `docker compose --profile migrate run --rm migrate`
5. API no host: **https://localhost:8080** (mapeamento `8080:5042`; Kestrel usa certificado de desenvolvimento gerado na imagem Docker — o navegador pode alertar até você confiar no certificado ou usar `-k` no `curl`).

### Opção B — `dotnet run` no host

1. SQL Server acessível (ex.: `localhost:1433` se usar o container só do `db`).
2. Ajuste `ConnectionStrings:DefaultConnection` em `AuthService/appsettings.Development.json` ou sobrescreva via variável de ambiente (veja tabela abaixo). A senha deve ser a **mesma** configurada no SQL (ex.: a do `.env` se o banco for o do Compose).
3. Aplicar migrations no banco alvo (veja [Migrations](#migrations-ef-core)).
4. Na pasta `AuthService`: `dotnet run`  
   URLs padrão do perfil HTTP: **http://localhost:5052** (veja `Properties/launchSettings.json`).

Todas as rotas da API REST ficam sob o prefixo **`/api/v1`** (ex.: `GET http://localhost:5052/api/v1/health`).

---

## Configuração e variáveis de ambiente

| Origem | Descrição |
|--------|-----------|
| `ConnectionStrings:DefaultConnection` | Connection string do SQL Server **com** banco (ex.: `Database=AuthServiceDb`). No Compose: `ConnectionStrings__DefaultConnection`. |
| `ASPNETCORE_ENVIRONMENT` | `Development`, `Production` ou `Testing`. Em **Testing**, não há redirecionamento HTTPS e o seed do catálogo no `Program` é omitido (testes fazem seed no *factory*). |
| `Auth:Jwt:Secret` | Segredo HMAC do JWT; **mínimo 32 caracteres**. Em produção, use segredo forte e armazenamento seguro — **não** commite valores reais. |
| `Auth:Jwt:ExpirationMinutes` | Validade do access token em minutos. |
| `DEFAULT_SYSTEM_USER_PASSWORD` | Credencial do usuário `root@email.com.br`. **Obrigatória** em Development e Production; fail-fast se ausente. No Docker Compose o default é `toor`. |
| `ADMIN_SYSTEM_USER_PASSWORD` | Credencial do usuário `admin@email.com.br`. **Obrigatória** em Development e Production; fail-fast se ausente. No Docker Compose o default é `admin`. |
| `DEFAULT_USER_PASSWORD` | Credencial do usuário `default@email.com.br`. **Obrigatória** em Development e Production; fail-fast se ausente. No Docker Compose o default é `default`. |
| `AUTH_SERVICE_TEST_SQL_BASE` | Obrigatória para **testes de integração**: connection string **sem** `Database` / `Initial Catalog`. |

Exemplo de override no shell (Linux):

```bash
export ConnectionStrings__DefaultConnection="Server=127.0.0.1,1433;Database=AuthServiceDb;User Id=sa;Password=SuaSenha;TrustServerCertificate=True"
export Auth__Jwt__Secret="sua-chave-com-pelo-menos-32-caracteres!!"
```

---

## Fluxo de inicialização da aplicação

1. **`WebApplication.CreateBuilder`** — registra `AppDbContext` (SQL Server), opções JWT, serviços de autenticação/autorização customizados, controllers e Swagger.
2. **Pipeline HTTP** — em ambientes diferentes de **Testing**, `UseHttpsRedirection`. **Swagger** e **Swagger UI** são registrados **antes** de autenticação/autorização, ficando **anônimos**.
3. **`UseAuthentication`** / **`UseAuthorization`** — JWT *handler* valida cabeçalho `Authorization: Bearer …`, *claims* e coerência com o usuário no banco (`TokenVersion`, ativo).
4. **`MapGroup("/api/v1").MapControllers()`** — todas as rotas de API ficam versionadas em `/api/v1`.
5. **Pós-build (Development e Production apenas)** — `OfficialCatalogSeeder.EnsureCatalogAsync` garante o catálogo oficial com apenas os sistemas `authenticator` e `kurtto`, além dos tipos e permissões no banco; em seguida `KurttoAccessSeeder.EnsureKurttoAccessAsync` garante rotas do Kurtto (ver [Seed do sistema Kurtto](#seed-do-sistema-kurtto-lfc-kurtto)); depois `DefaultSystemUserSeeder.EnsureDefaultUserAsync` garante os usuários base `root@email.com.br`, `admin@email.com.br` e `default@email.com.br` com vínculos às permissões do catálogo, de forma idempotente. As credenciais são lidas das variáveis de ambiente `DEFAULT_SYSTEM_USER_PASSWORD`, `ADMIN_SYSTEM_USER_PASSWORD` e `DEFAULT_USER_PASSWORD` (fail-fast se ausentes); no banco persiste-se **somente o hash** PBKDF2. **Em produção, defina valores fortes e troque as senhas imediatamente após o primeiro acesso.**

### Seed do sistema Kurtto (`lfc-kurtto`)

O repositório **[lfc-kurtto](https://github.com/LF-Calegari/lfc-kurtto)** expõe a API sob `/api/v1`. Hoje, as operações que exigem credencial usam o header **`X-Admin-Secret`** (variável `ADMIN_API_SECRET` no Kurtto), não JWT do auth-service. O seed deste serviço prepara o cadastro para alinhar **permissões `perm:Kurtto.*`** e **rotas** no banco quando um *gateway* ou o próprio Kurtto passar a validar JWT.

Execução: automática após o catálogo oficial (`KurttoAccessSeeder.EnsureKurttoAccessAsync` em `Program.cs` e no *factory* de testes). É **idempotente** (reexecução não duplica rotas).

- **Sistema** `kurtto` (código `kurtto`) e permissões oficiais `create` / `read` / `update` / `delete` / `restore` (políticas `perm:Kurtto.Create`, …, `perm:Kurtto.Restore`).
- **Rotas cadastradas** (`Routes.Code`), espelhando as superfícies que checam admin no Kurtto (`src/controllers/UrlController.ts`, `requireAdminOperation` de `src/utils/adminAuth.ts`):

| `Routes.Code` | Superfície Kurtto | Política JWT alvo (auth-service) |
|----------------|-------------------|----------------------------------|
| `KURTTO_V1_URLS_LIST_INCLUDE_DELETED` | `GET /api/v1/urls` com `include_deleted=true` | `perm:Kurtto.Read` |
| `KURTTO_V1_URLS_GET_BY_CODE_INCLUDE_DELETED` | `GET /api/v1/urls/{code}` com `include_deleted=true` | `perm:Kurtto.Read` |
| `KURTTO_V1_URLS_PATCH_RESTORE` | `PATCH /api/v1/urls/{code}/restore` | `perm:Kurtto.Restore` |

**Checklist de auditoria (cobertura das rotas credenciadas no Kurtto):** no repositório `lfc-kurtto`, todas as chamadas a `requireAdminOperation` estão no `UrlController` (listagem e leitura com `include_deleted`, e `restore`). Não há outros controllers com esse *gate* na API `/api/v1`. Os demais endpoints de URLs (`POST`, `PATCH` sem restore, `DELETE`) e os *health checks* (`/api/v1/health`, `/live`, `/ready`) permanecem **anônimos** no Kurtto atual; não entram neste seed até haver exigência explícita de credencial nelas.

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
| `GET` | `/api/v1/auth/logout` | Sim | — |

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
| `DELETE` | `/api/v1/systems/routes/{id}` | Sim | `perm:SystemsRoutes.Delete` |
| `POST` | `/api/v1/systems/routes/{id}/restore` | Sim | `perm:SystemsRoutes.Restore` |

`systemId` obrigatório; `code` único globalmente. Listagens consideram sistema pai ativo.

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

**POST:** `name`, `email`, `password`, `identity`, `active` (opcional, padrão `true`). **PUT** usuário: `name`, `email`, `identity`, `active` (sem senha). Email normalizado (ex.: minúsculas).

**GET** `/api/v1/users/{id}` retorna também os vínculos ativos **`roles`** (lista com `id` inteiro, `userId`, `roleId`, auditoria e `deletedAt`) e **`permissions`** (lista com `id` GUID, `userId`, `permissionId`, auditoria e `deletedAt`). Listagens **`GET /api/v1/users`** e respostas de criação/atualização devolvem `roles` e `permissions` como arrays vazios.

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

### Permissões — `/api/v1/permissions`

| Método | Endpoint | Auth | Permissão |
|--------|----------|------|-----------|
| `POST` | `/api/v1/permissions` | Sim | `perm:Permissions.Create` |
| `GET` | `/api/v1/permissions` | Sim | `perm:Permissions.Read` |
| `GET` | `/api/v1/permissions/{id}` | Sim | `perm:Permissions.Read` |
| `PUT` | `/api/v1/permissions/{id}` | Sim | `perm:Permissions.Update` |
| `DELETE` | `/api/v1/permissions/{id}` | Sim | `perm:Permissions.Delete` |
| `POST` | `/api/v1/permissions/{id}/restore` | Sim | `perm:Permissions.Restore` |

Corpo: `systemId`, `permissionTypeId`, `description` (opcional). Restauração exige referências ativas coerentes com as regras do controller.

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

**Verificar token e permissões efetivas (ids)**

```bash
curl -s "http://localhost:5052/api/v1/auth/verify-token" \
  -H "Authorization: Bearer SEU_JWT_AQUI"
```

Resposta (200): objeto com `id`, `name`, `email`, `identity` e `permissions` (lista de GUIDs).

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

Este serviço faz parte de um **sistema maior**: em Docker, ele deve usar a **mesma rede externa** que os demais projetos (por exemplo, **Kurtto Service**) para que os containers se comuniquem por nome de host interno.

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
| `db` | SQL Server 2022; porta **1433** no host; volume `mssql_data`. |
| `app` | API com `dotnet watch`, volume `./AuthService:/app`, porta **8080→5042** (HTTPS no container). |
| `migrate` (profile `migrate`) | `dotnet ef database update` contra o `db`. |
| `test` (profile `test`) | Executa `dotnet test` com `AUTH_SERVICE_TEST_SQL_BASE` apontando para `db`. |

**Subir stack:** `docker compose up -d --build`  
**Migrations:** `docker compose --profile migrate run --rm migrate`  
**Testes de integração:** `docker compose --profile test run --rm test`

Com o `app` em execução, migrations manuais no container:

```bash
docker compose exec app sh -c "dotnet restore && dotnet tool restore && dotnet ef database update"
```

String típica **na rede do Compose** (servidor `db`):

`Server=db,1433;Database=AuthServiceDb;User Id=sa;Password=<sua senha>;TrustServerCertificate=True`

---

## Migrations (EF Core)

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
  - **Integração**: usa SQL Server real e sobe a aplicação completa em memória.
- Para executar apenas testes unitários:

```bash
dotnet test AuthService.Tests/AuthService.Tests.csproj --filter "FullyQualifiedName~UnitTests"
```

- Utilizam **SQL Server real**; cada execução cria um banco `auth_svc_it_<guid>`, aplica migrations, executa seeders de catálogo, seed Kurtto (`KurttoAccessSeeder`) e usuários base (`root/admin/default`), e remove o banco ao final.
- **Obrigatório** definir `AUTH_SERVICE_TEST_SQL_BASE` **sem** catálogo inicial:

```bash
export AUTH_SERVICE_TEST_SQL_BASE="Server=127.0.0.1,1433;User Id=sa;Password=<MESMA_DO_.env>;TrustServerCertificate=True"
dotnet test AuthService.Tests/AuthService.Tests.csproj
```

Sem essa variável, o `WebApplicationFactory` falha na construção — comportamento esperado.

---

## Troubleshooting

| Sintoma | Verificação sugerida |
|---------|----------------------|
| Falha ao conectar ao SQL | Senha/porta/host; `TrustServerCertificate=True` em dev; firewall. |
| `dotnet ef` não encontrado | `dotnet tool install --global dotnet-ef` ou `dotnet tool restore` no projeto. |
| 401 em todas as rotas protegidas | Cabeçalho `Authorization: Bearer`; relógio do cliente; token expirado ou após **logout**. |
| 403 em recurso específico | Usuário não possui GUID da permissão correspondente à política `perm:…` (vincular via papéis, permissões diretas no usuário ou processos de dados; consulte **`GET /api/v1/users/{id}`** para os vínculos). |
| 404 em entidade “que existe” | Pode estar *soft-deleted*; usar rota de **restore** quando aplicável. |
| Docker `app` não sobe | Aguardar healthcheck do `db`; conferir `MSSQL_SA_PASSWORD` no `.env`. |
| Caminho 404 na API | Prefixo **`/api/v1`** obrigatório em todos os controllers mapeados. |

---

## Boas práticas e segurança

- **Nunca** commitar segredos reais; use variáveis de ambiente ou cofres em produção.
- Troque `Auth:Jwt:Secret` em qualquer ambiente exposto; o repositório contém valores apenas para desenvolvimento.
- **HTTPS** em produção; em desenvolvimento local o perfil pode usar só HTTP.
- Após alterar permissões ou papéis, clientes podem chamar **`/api/v1/auth/verify-token`** para obter a lista atualizada de `permissions`.
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

**SQL Server** (este repositório):

```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet add package Microsoft.EntityFrameworkCore.SqlServer
```

**PostgreSQL**

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
