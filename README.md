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

- CRUD com *soft delete* e restore para **`/systems`**, **`/users`** e **`/routes`**.
- **`/routes`** está vinculada a **sistema ativo** (`systemId`); leituras e restore respeitam o sistema pai não deletado.
- Testes de integração com SQL Server real (criação e descarte de banco por execução).

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

### Saúde

| Método | Rota | Descrição |
|--------|------|-----------|
| `GET` | `/health` | Verificação de saúde da aplicação. |

### Sistemas (`/systems`)

Registros com *soft delete* (`deletedAt` preenchido) respondem **404** em leitura, atualização e exclusão; a exceção é **`PATCH .../restore`**.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/systems` | Cria (`name`, `code`, `description` opcional). |
| `GET` | `/systems` | Lista apenas ativos. |
| `GET` | `/systems/{id}` | Detalhe (ativo). |
| `PUT` | `/systems/{id}` | Atualização completa. |
| `DELETE` | `/systems/{id}` | *Soft delete*. |
| `PATCH` | `/systems/{id}/restore` | Restaura registro deletado. |

### Usuários (`/users`)

Mesmo padrão de *soft delete* e **404** para deletados que `/systems` (exceto `PATCH /users/{id}/restore`). Email único (normalizado em minúsculas). Corpo: `name`, `email`, `password`, `identity`, `active` (opcional no POST; padrão `true`).

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/users` | Cria usuário. |
| `GET` | `/users` | Lista ativos. |
| `GET` | `/users/{id}` | Detalhe (ativo). |
| `PUT` | `/users/{id}` | Atualização completa. |
| `DELETE` | `/users/{id}` | *Soft delete*. |
| `PATCH` | `/users/{id}/restore` | Restaura registro deletado. |

### Routes (`/routes`)

Vinculadas a **sistema existente e ativo** (`systemId`). *Soft delete* e **404** como em `/systems`. `Code` é **único globalmente**. Corpo: `systemId`, `name`, `code`, `description` (opcional).

Nos **`GET`**, só aparecem routes cujo **sistema pai** ainda está ativo; se o sistema for *soft-deleted*, a route some da API até o sistema ser restaurado. **`PATCH .../restore`** da route exige sistema ativo.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/routes` | Cria (`systemId` obrigatório e ativo). |
| `GET` | `/routes` | Lista routes ativas com sistema pai ativo. |
| `GET` | `/routes/{id}` | Detalhe (route ativa + sistema pai ativo). |
| `PUT` | `/routes/{id}` | Atualização completa. |
| `DELETE` | `/routes/{id}` | *Soft delete*. |
| `PATCH` | `/routes/{id}/restore` | Restaura route deletada (sistema ativo). |

Em ambiente **Development**, a especificação OpenAPI fica em **`/openapi/v1.json`**.

## Testes de integração

A suite usa **SQL Server real**. Cada execução cria um banco dedicado (`auth_svc_it_<guid>`), aplica **migrations** e faz **DROP DATABASE** ao terminar (adequado para rodar testes em paralelo). Há cobertura para **`/systems`**, **`/users`** e **`/routes`**.

Defina a connection string **sem** `Database` / `Initial Catalog`:

```bash
export AUTH_SERVICE_TEST_SQL_BASE="Server=127.0.0.1,1433;User Id=sa;Password=<MESMA_DO_.env>;TrustServerCertificate=True"
dotnet test AuthService.Tests/AuthService.Tests.csproj
```

**Alternativa:** `docker compose --profile test run --rm test` (veja acima).

Sem `AUTH_SERVICE_TEST_SQL_BASE`, a criação do `WebAppFactory` falha — comportamento esperado.

## Roadmap

1. Entidade **Recurso** e relação com **Rotas** conforme o modelo alvo (hoje `Routes` liga-se diretamente ao sistema).
2. Gestão de **permissões** por recurso/rota.
3. **Autenticação** (tokens/sessão) e **políticas de autorização** na API.
4. Evolução da documentação OpenAPI e mais cenários de teste.

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
