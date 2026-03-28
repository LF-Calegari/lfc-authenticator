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

Nesta fase inicial, o serviço expõe apenas o endpoint de saúde:

- `GET /health`

Resposta esperada: status da aplicação ativo/saudável.

## Próximos passos

1. Definir entidades e relacionamento no banco de dados.
2. Implementar CRUD de Sistema, Recurso e Rotas.
3. Implementar gestão de permissões por recurso/rota.
4. Adicionar autenticação e política de autorização.
5. Criar testes automatizados e documentação da API.


## Comandos Docker
1. Criar um novo projeto
```bash
docker run --rm -it \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet new webapi --use-controllers -n MeuProjeto
```