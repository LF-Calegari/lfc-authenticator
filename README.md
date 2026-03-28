# AuthService

Serviço de autenticação e autorização responsável por controlar permissões de acesso no ecossistema do projeto.

## Objetivo

O **AuthService** centraliza a gestão de permissões e define quais ações cada perfil/usuário pode executar em cada recurso do sistema.

## Escopo funcional

O serviço terá os seguintes cadastros principais:

1. **Cadastro de Sistema**
2. **Cadastro de Recurso**
3. **Cadastro de Rotas**
4. **Cadastro de Permissões**

As permissões disponíveis por padrão são:

- `create`
- `read`
- `update`
- `delete`
- `restore`

## Modelo de autorização (visão inicial)

- Um **Sistema** possui vários **Recursos**.
- Um **Recurso** possui uma ou mais **Rotas**.
- Uma **Permissão** define quais ações (`create`, `read`, `update`, `delete`, `restore`) podem ser realizadas em um recurso/rota.
- Perfis, usuários ou integrações externas poderão ser associados a permissões (definição detalhada nas próximas versões).

## Desenvolvimento com Docker

Arquivo na raiz: **`docker-compose.yml`**.

1. **Variáveis de ambiente:** `cp .env.example .env` e edite se precisar. A senha `MSSQL_SA_PASSWORD` deve ser **a mesma** que está em `AuthService/appsettings.Development.json` se você rodar `dotnet run` **fora** do Docker com SQL em `localhost:1433` (o padrão do exemplo é `DevPassword123!`).
2. **Subir API + SQL:** `docker compose up -d --build`  
   O serviço `app` só sobe depois do `db` ficar **healthy** (healthcheck com `sqlcmd`).
3. **Migrations (primeira vez ou após alterar migrations):**
   ```bash
   docker compose --profile migrate run --rm migrate
   ```
4. **API:** `http://localhost:8080` (mapeamento `8080:5042` → app escuta em `5042` no container).

O SQL Server usa o volume `mssql_data` e expõe **`localhost:1433`** no host.

**Dentro do Compose**, a connection string é injetada no container `app` via `ConnectionStrings__DefaultConnection` com a senha do `.env` (não depende da senha fixa antiga no JSON).

### Testes de integração via Compose

Com o stack (pelo menos o `db`) no ar:

```bash
docker compose --profile test run --rm test
```

O serviço `test` monta a **raiz do repositório** e define `AUTH_SERVICE_TEST_SQL_BASE` apontando para o host `db` na rede interna.

## Endpoints iniciais

- `GET /health` — saúde da aplicação.

### Cadastro de sistemas (`/systems`)

Rotas REST (JSON). Registros com *soft delete* (`deletedAt` preenchido) retornam **404** em todas as operações exceto `PATCH .../restore`.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/systems` | Cria sistema (`name`, `code`, `description` opcional). |
| `GET` | `/systems` | Lista apenas sistemas ativos (não deletados). |
| `GET` | `/systems/{id}` | Detalhe por id (ativo). |
| `PUT` | `/systems/{id}` | Atualização completa. |
| `DELETE` | `/systems/{id}` | *Soft delete*. |
| `PATCH` | `/systems/{id}/restore` | Restaura registro deletado. |

Em desenvolvimento, a documentação OpenAPI fica em `/openapi/v1.json` quando `Development`.

## Testes de integração

Os testes usam **SQL Server real**. Cada caso cria um banco dedicado (`auth_svc_it_<guid>`), roda **migrations** e faz **DROP DATABASE** ao terminar (seguro para paralelismo).

**Variável obrigatória** (connection string **sem** `Database` / `Initial Catalog`):

```bash
export AUTH_SERVICE_TEST_SQL_BASE="Server=127.0.0.1,1433;User Id=sa;Password=<MESMA_DO_.env>;TrustServerCertificate=True"
dotnet test AuthService.Tests/AuthService.Tests.csproj
```

**Alternativa:** `docker compose --profile test run --rm test` (veja *Desenvolvimento com Docker*).

Sem `AUTH_SERVICE_TEST_SQL_BASE`, a suite falha ao criar o `WebAppFactory` (esperado).

**Migrações com o `app` já rodando** (volume só com `AuthService`):

```bash
docker compose exec app sh -c "dotnet restore && dotnet tool restore && dotnet ef database update"
```

## Próximos passos

1. Implementar CRUD de Recurso e Rotas.
2. Implementar gestão de permissões por recurso/rota.
3. Adicionar autenticação e política de autorização.
4. Expandir documentação OpenAPI e cenários de teste.


## Comandos Docker
1. Criar um novo projeto
```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet new webapi --use-controllers -n MeuProjeto
```