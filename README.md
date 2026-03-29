# AuthService

Serviço de **autenticação e autorização** do ecossistema: centraliza cadastros e regras que definem o que cada usuário ou integração pode fazer em cada parte do sistema.

## Objetivo

O AuthService concentra a gestão de permissões e padroniza como perfis, usuários ou integrações se relacionam com recursos e rotas da API.

## Domínio (visão do produto)

### Cadastros previstos

1. **Sistema**
2. **Recurso** (agrupamento lógico dentro de um sistema)
3. **Rota** (endpoints ou caminhos associados a recursos)
4. **Permissão** (liga ações a recursos/rotas)

### Ações padrão

`create`, `read`, `update`, `delete`, `restore`

### Modelo de autorização (alvo)

- Um **Sistema** agrupa vários **Recursos**.
- Um **Recurso** possui uma ou mais **Rotas**.
- Uma **Permissão** indica quais ações são permitidas sobre recurso/rota.
- Perfis, usuários ou integrações externas serão associados a permissões (detalhes nas próximas versões).

### Implementado hoje

- **Autenticação JWT** (`POST /auth/login`, `GET /auth/verify-token`, `GET /auth/logout`) e **autorização** por permissão oficial (políticas `perm:Recurso.Ação` resolvidas no banco).
- **`GET /health`** sem autenticação; demais rotas exigem **Bearer**, salvo `POST /auth/login`.
- Catálogo oficial de sistemas/tipos de permissão/linhas de permissão é criado de forma **idempotente** nos testes (após migrations) e na subida em **Development/Production** (fora do ambiente `Testing`).
- CRUD com *soft delete* e **`POST .../restore`** para **`/systems`**, **`/systems/routes`**, **`/users`** (inclui **`PUT /users/{id}/password`**), **`/permissions/types`**, **`/tokens/types`**, **`/roles`**, **`/permissions`**, além de **`/users-roles`**, **`/users-permissions`** e **`/roles-permissions`** (estes últimos exigem apenas usuário autenticado).
- **`/systems/routes`** segue o mesmo vínculo a **sistema ativo** (`systemId`) que as routes já tinham.
- Testes de integração com SQL Server real (banco dedicado por execução).

## Desenvolvimento com Docker

O arquivo **`docker-compose.yml`** fica na **raiz** do repositório.

1. **Ambiente:** `cp .env.example .env` e ajuste se precisar. A senha `MSSQL_SA_PASSWORD` deve ser a **mesma** usada em `AuthService/appsettings.Development.json` se você rodar `dotnet run` **no host** apontando para `localhost:1433` (o exemplo padrão usa `DevPassword123!`).
2. **Subir API + SQL:** `docker compose up -d --build`  
   O serviço `app` aguarda o `db` ficar **healthy** (healthcheck com `sqlcmd` e certificado confiável em dev).
3. **Primeira carga ou após alterar migrations:**
   ```bash
   docker compose --profile migrate run --rm migrate
   ```
4. **API:** [http://localhost:8080](http://localhost:8080) (mapeamento `8080:5042`; a app escuta na porta `5042` dentro do container).

O SQL Server usa o volume **`mssql_data`** e expõe **`localhost:1433`** no host. No Compose, a connection string do `app` vem de `ConnectionStrings__DefaultConnection` com a senha do `.env` (não depende de senha fixa só no JSON).

**Exemplo de string na rede interna do Compose** (host `db`):

`Server=db,1433;User Id=sa;Password=<sua senha>;TrustServerCertificate=True;`

### Testes via Compose

Com o stack (no mínimo o `db`) no ar:

```bash
docker compose --profile test run --rm test
```

O serviço `test` monta a raiz do repositório e define `AUTH_SERVICE_TEST_SQL_BASE` apontando para o host `db`.

### Aplicar migrations com o `app` já em execução

Útil quando o volume monta só `AuthService`:

```bash
docker compose exec app sh -c "dotnet restore && dotnet tool restore && dotnet ef database update"
```

## API REST

Todas as rotas abaixo (exceto **`GET /health`** e **`POST /auth/login`**) exigem cabeçalho **`Authorization: Bearer <jwt>`** e a **permissão oficial** indicada entre parênteses (ex.: `Systems.Read`).

### Saúde

| Método | Rota | Autenticação | Descrição |
|--------|------|--------------|-----------|
| `GET` | `/health` | Não | Verificação de saúde da aplicação. |

### Autenticação (`/auth`)

| Método | Rota | Autenticação | Descrição |
|--------|------|--------------|-----------|
| `POST` | `/auth/login` | Não | Login com `email` e `password`; retorna JWT. |
| `GET` | `/auth/verify-token` | Sim | Valida JWT e retorna usuário e ids de permissões efetivas. |
| `GET` | `/auth/logout` | Sim | Invalida sessões anteriores (incrementa `tokenVersion`). |

### Sistemas (`/systems`) — (`Systems.*`)

Registros com *soft delete* respondem **404** em leitura, atualização e exclusão; exceção: **`POST .../restore`**.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/systems` | Cria (`name`, `code`, `description` opcional). |
| `GET` | `/systems` | Lista apenas ativos. |
| `GET` | `/systems/{id}` | Detalhe (ativo). |
| `PUT` | `/systems/{id}` | Atualização completa. |
| `DELETE` | `/systems/{id}` | *Soft delete*. |
| `POST` | `/systems/{id}/restore` | Restaura registro deletado. |

### Usuários (`/users`) — (`Users.*`)

Mesmo padrão de *soft delete* e **404** que `/systems`. Email único (normalizado). **POST** usa `name`, `email`, `password`, `identity`, `active` (opcional; padrão `true`). **PUT** `/users/{id}` usa `name`, `email`, `identity`, `active` (sem senha). **PUT** `/users/{id}/password` usa apenas `password`.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/users` | Cria usuário. |
| `GET` | `/users` | Lista ativos. |
| `GET` | `/users/{id}` | Detalhe (ativo). |
| `PUT` | `/users/{id}` | Atualização de dados (sem senha). |
| `PUT` | `/users/{id}/password` | Atualiza somente a senha. |
| `DELETE` | `/users/{id}` | *Soft delete*. |
| `POST` | `/users/{id}/restore` | Restaura registro deletado. |

### Routes (`/systems/routes`) — (`SystemsRoutes.*`)

Vinculadas a **sistema existente e ativo** (`systemId`). `Code` **único globalmente**. Corpo: `systemId`, `name`, `code`, `description` (opcional). Nos **`GET`**, só entram routes com sistema pai ativo. **`POST .../restore`** exige sistema ativo.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/systems/routes` | Cria. |
| `GET` | `/systems/routes` | Lista routes ativas com sistema pai ativo. |
| `GET` | `/systems/routes/{id}` | Detalhe. |
| `PUT` | `/systems/routes/{id}` | Atualização completa. |
| `DELETE` | `/systems/routes/{id}` | *Soft delete*. |
| `POST` | `/systems/routes/{id}/restore` | Restaura route deletada. |

### Token types (`/tokens/types`) — (`SystemTokensTypes.*`)

Mesmo padrão de *soft delete* que **`/systems`**. Corpo: `name`, `code`, `description` (opcional).

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/tokens/types` | Cria registro. |
| `GET` | `/tokens/types` | Lista ativos. |
| `GET` | `/tokens/types/{id}` | Detalhe. |
| `PUT` | `/tokens/types/{id}` | Atualização completa. |
| `DELETE` | `/tokens/types/{id}` | *Soft delete*. |
| `POST` | `/tokens/types/{id}/restore` | Restaura registro deletado. |

### Permission types (`/permissions/types`) — (`PermissionsTypes.*`)

Mesmo padrão de *soft delete* que **`/systems`**. `Code` **único globalmente**. Corpo: `name`, `code`, `description` (opcional).

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/permissions/types` | Cria registro. |
| `GET` | `/permissions/types` | Lista ativos. |
| `GET` | `/permissions/types/{id}` | Detalhe. |
| `PUT` | `/permissions/types/{id}` | Atualização completa. |
| `DELETE` | `/permissions/types/{id}` | *Soft delete*. |
| `POST` | `/permissions/types/{id}/restore` | Restaura registro deletado. |

### Roles (`/roles`) — (`Roles.*`)

Mesmo padrão de *soft delete* que **`/systems`**. Corpo: `name`, `code`.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/roles` | Cria registro. |
| `GET` | `/roles` | Lista ativos. |
| `GET` | `/roles/{id}` | Detalhe. |
| `PUT` | `/roles/{id}` | Atualização completa. |
| `DELETE` | `/roles/{id}` | *Soft delete*. |
| `POST` | `/roles/{id}/restore` | Restaura registro deletado. |

### Permissions (`/permissions`) — (`Permissions.*`)

Vinculadas a **sistema** e **tipo de permissão** ativos. Corpo: `systemId`, `permissionTypeId`, `description` (opcional). **`POST .../restore`** exige referências ativas.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/permissions` | Cria. |
| `GET` | `/permissions` | Lista permissões ativas (filtros de pai ativo). |
| `GET` | `/permissions/{id}` | Detalhe. |
| `PUT` | `/permissions/{id}` | Atualização completa. |
| `DELETE` | `/permissions/{id}` | *Soft delete*. |
| `POST` | `/permissions/{id}/restore` | Restaura permissão deletada. |

### Users–roles (`/users-roles`)

Vínculo **usuário ↔ papel** (`userId`, `roleId`). Par **único** globalmente (inclui registros *soft-deleted*). Chaves estrangeiras para **`Users`** e **`Roles`** com exclusão/atualização **restritas**. `Id` numérico com **identity** no SQL Server. *Soft delete* e **404** para deletados (exceto `PATCH .../restore`).

Nos **`GET`**, só aparecem vínculos cujo **usuário** e **papel** ainda estão ativos. **`PATCH .../restore`** exige ambos ativos.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/users-roles` | Cria vínculo (`userId`, `roleId` obrigatórios e ativos). |
| `GET` | `/users-roles` | Lista vínculos ativos com usuário e papel ativos. |
| `GET` | `/users-roles/{id}` | Detalhe (`id` inteiro). |
| `PUT` | `/users-roles/{id}` | Atualização completa do par. |
| `DELETE` | `/users-roles/{id}` | *Soft delete*. |
| `PATCH` | `/users-roles/{id}/restore` | Restaura vínculo deletado (referências ativas). |

Em ambiente **Development**, a especificação OpenAPI fica em **`/openapi/v1.json`**.

## Testes de integração

A suite usa **SQL Server real**. Cada execução cria um banco dedicado (`auth_svc_it_<guid>`), aplica **migrations**, garante o **catálogo oficial de permissões** e o usuário bootstrap de integração, e faz **DROP DATABASE** ao terminar. Há cobertura para autenticação, **`/systems`**, **`/users`**, **`/systems/routes`**, **`/permissions/types`**, **`/tokens/types`**, **`/roles`**, **`/permissions`**, **`/users-roles`**, vínculos de permissões e papéis.

Defina a connection string **sem** `Database` / `Initial Catalog`:

```bash
export AUTH_SERVICE_TEST_SQL_BASE="Server=127.0.0.1,1433;User Id=sa;Password=<MESMA_DO_.env>;TrustServerCertificate=True"
dotnet test AuthService.Tests/AuthService.Tests.csproj
```

**Alternativa:** `docker compose --profile test run --rm test` (veja acima).

Sem `AUTH_SERVICE_TEST_SQL_BASE`, a criação do `WebAppFactory` falha — comportamento esperado.

## Roadmap

1. Entidade **Recurso** e relação com **Rotas** conforme o modelo alvo (hoje `Routes` liga-se diretamente ao sistema).
2. Refinamento de **permissões** por recurso/rota e matriz de perfis.
3. Evolução da documentação OpenAPI e mais cenários de teste (incluindo 403 por permissão ausente).

## Apêndice: SDK .NET em container

Comandos úteis ao criar outro projeto ou adicionar pacotes sem instalar o SDK no host (imagem `mcr.microsoft.com/dotnet/sdk:10.0`).

### Novo projeto Web API

```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet new webapi --use-controllers -n MeuProjeto
```

### Pacotes Entity Framework (exemplos)

**SQL Server** (o que este repositório usa):

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

**CLI `dotnet-ef`** (global na sessão do container)

```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet tool install --global dotnet-ef
```
