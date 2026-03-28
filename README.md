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

1. Copie `.env.example` para `.env` (ou mantenha o `.env` local já criado).
2. Ajuste `MSSQL_SA_PASSWORD` se necessário (a Microsoft exige senha forte).
3. Suba os serviços: `docker compose up -d`.

O SQL Server usa volume nomeado Docker (`mssql_data`) para os dados e escuta em `localhost:1433`. O healthcheck usa `sqlcmd` com `-C` (certificado autoassinado em dev).

**Connection string (app no mesmo Compose):** use o host `db` e a mesma senha do `.env`, por exemplo:

`Server=db,1433;User Id=sa;Password=<sua senha>;TrustServerCertificate=True;`

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

Na raiz do repositório (com SDK .NET 10):

```bash
dotnet test AuthService.Tests/AuthService.Tests.csproj
```

O compose de desenvolvimento monta só `./AuthService` em `/app`, então **não existe** `AuthService.Tests` dentro do container. Para testes com Docker, monte a **raiz do repositório**:

```bash
docker run --rm -v "$PWD:/workspace" -w /workspace mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test AuthService.Tests/AuthService.Tests.csproj
```

Migrações com o serviço `app` já em execução:

```bash
docker compose exec app dotnet restore
docker compose exec app dotnet ef database update
```

O `restore` evita erros de pacote quando o `obj/` está desatualizado em relação ao `.csproj`.

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