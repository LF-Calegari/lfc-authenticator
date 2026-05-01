using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using AuthService.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class ClientsApiTests : IAsyncLifetime
{
    private static readonly int[] CnpjWeights1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
    private static readonly int[] CnpjWeights2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
    private WebAppFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebAppFactory();
        _client = await TestApiClient.CreateAuthenticatedAsync(_factory);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreatePfClient_ThenGetById_ReturnsCreated()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000000),
            fullName = "Cliente PF Teste"
        }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var dto = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("PF", dto.Type);
        Assert.Equal(GenerateCpf(100000000), dto.Cpf);
        Assert.Equal("Cliente PF Teste", dto.FullName);
        Assert.Null(dto.Cnpj);
        Assert.Null(dto.CorporateName);

        var get = await _client.GetAsync($"/api/v1/clients/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task CreatePjClient_WithInvalidCnpj_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PJ",
            cnpj = "12345678000100",
            corporateName = "Empresa Inválida"
        }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePjClient_UpdateAndGetById_FullFlowWorks()
    {
        var cnpj = GenerateCnpj(12000000);
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PJ",
            cnpj,
            corporateName = "Empresa Inicial LTDA"
        }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("PJ", created.Type);
        Assert.Equal(cnpj, created.Cnpj);
        Assert.Equal("Empresa Inicial LTDA", created.CorporateName);
        Assert.Null(created.Cpf);
        Assert.Null(created.FullName);

        var update = await _client.PutAsJsonAsync($"/api/v1/clients/{created.Id}", new
        {
            type = "PJ",
            cnpj,
            corporateName = "Empresa Atualizada LTDA"
        }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Empresa Atualizada LTDA", updated.CorporateName);

        var get = await _client.GetAsync($"/api/v1/clients/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var detail = await get.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal("Empresa Atualizada LTDA", detail.CorporateName);
    }

    [Fact]
    public async Task CreatePfClient_DuplicateCpf_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = "52998224725",
            fullName = "Cliente 1"
        }, TestApiClient.JsonOptions);

        var second = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = "52998224725",
            fullName = "Cliente 2"
        }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreatePfClient_DuplicateCpfInParallel_ReturnsCreatedAndConflict()
    {
        var cpf = GenerateCpf(100000020);
        var request = new
        {
            type = "PF",
            cpf,
            fullName = "Cliente Concorrencia PF"
        };

        var responses = await Task.WhenAll(
            _client.PostAsJsonAsync("/api/v1/clients", request, TestApiClient.JsonOptions),
            _client.PostAsJsonAsync("/api/v1/clients", request, TestApiClient.JsonOptions));

        var statuses = responses.Select(r => r.StatusCode).ToArray();
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    [Fact]
    public async Task CreatePjClient_DuplicateCnpjInParallel_ReturnsCreatedAndConflict()
    {
        var cnpj = GenerateCnpj(12000020);
        var request = new
        {
            type = "PJ",
            cnpj,
            corporateName = "Empresa Concorrencia LTDA"
        };

        var responses = await Task.WhenAll(
            _client.PostAsJsonAsync("/api/v1/clients", request, TestApiClient.JsonOptions),
            _client.PostAsJsonAsync("/api/v1/clients", request, TestApiClient.JsonOptions));

        var statuses = responses.Select(r => r.StatusCode).ToArray();
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    [Fact]
    public async Task UpdateClient_ChangingType_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000001),
            fullName = "Cliente PF"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        var update = await _client.PutAsJsonAsync($"/api/v1/clients/{created.Id}", new
        {
            type = "PJ",
            cnpj = "11222333000181",
            corporateName = "Empresa X"
        }, TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task AddExtraEmails_MaxThree_ReturnsBadRequestOnFourth()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000002),
            fullName = "Cliente Email"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        for (var i = 1; i <= 3; i++)
        {
            var add = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/emails",
                new { email = $"extra{i}@example.com" }, TestApiClient.JsonOptions);
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        }

        var fourth = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/emails",
            new { email = "extra4@example.com" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
    }

    [Fact]
    public async Task AddExtraEmail_UsedAsUsername_ReturnsConflict()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000003),
            fullName = "Cliente Username"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        var add = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/emails",
            new { email = RootUserSeeder.RootEmail }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
    }

    [Fact]
    public async Task AddExtraEmail_DuplicateForSameClient_ReturnsConflict()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000021),
            fullName = "Cliente Email Duplicado"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        var first = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/emails",
            new { email = "duplicado@example.com" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/emails",
            new { email = "duplicado@example.com" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task RemoveExtraEmail_WhenUsedAsUsername_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000004),
            fullName = "Cliente Remoção"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        Guid contactId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = new ClientEmail
            {
                ClientId = created.Id,
                Email = RootUserSeeder.RootEmail,
                CreatedAt = DateTime.UtcNow
            };
            db.ClientEmails.Add(row);
            await db.SaveChangesAsync();
            contactId = row.Id;
        }

        var remove = await _client.DeleteAsync($"/api/v1/clients/{created.Id}/emails/{contactId}");
        Assert.Equal(HttpStatusCode.BadRequest, remove.StatusCode);
    }

    [Fact]
    public async Task RemoveExtraEmail_RemovesAndSecondAttemptReturnsNotFound()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000022),
            fullName = "Cliente Remocao Email"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        var add = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/emails",
            new { email = "remover@example.com" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var email = await add.Content.ReadFromJsonAsync<ClientEmailDto>(TestApiClient.JsonOptions);
        Assert.NotNull(email);

        var remove = await _client.DeleteAsync($"/api/v1/clients/{created.Id}/emails/{email.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var secondRemove = await _client.DeleteAsync($"/api/v1/clients/{created.Id}/emails/{email.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondRemove.StatusCode);
    }

    [Fact]
    public async Task AddMobile_InvalidFormat_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000005),
            fullName = "Cliente Fone"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        var add = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/mobiles",
            new { number = "18999999999" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
    }

    [Fact]
    public async Task AddMobile_MaxThree_ReturnsBadRequestOnFourth()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000006),
            fullName = "Cliente Limite Fone"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        for (var i = 1; i <= 3; i++)
        {
            var add = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/mobiles",
                new { number = $"+55189999999{i:00}" }, TestApiClient.JsonOptions);
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        }

        var fourth = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/mobiles",
            new { number = "+5518999999999" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
    }

    [Fact]
    public async Task AddPhone_InvalidFormat_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000008),
            fullName = "Cliente Telefone"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        var add = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/phones",
            new { number = "18999999999" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
    }

    [Fact]
    public async Task AddPhone_MaxThree_ReturnsBadRequestOnFourth()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000009),
            fullName = "Cliente Limite Telefone"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        for (var i = 1; i <= 3; i++)
        {
            var add = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/phones",
                new { number = $"+55183333333{i:00}" }, TestApiClient.JsonOptions);
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        }

        var fourth = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/phones",
            new { number = "+5518333333399" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
    }

    [Fact]
    public async Task RemovePhone_RemovesAndSecondAttemptReturnsNotFound()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000023),
            fullName = "Cliente Remocao Telefone"
        }, TestApiClient.JsonOptions);
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        var add = await _client.PostAsJsonAsync($"/api/v1/clients/{created.Id}/phones",
            new { number = "+5518333333311" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var phone = await add.Content.ReadFromJsonAsync<ClientPhoneDto>(TestApiClient.JsonOptions);
        Assert.NotNull(phone);

        var remove = await _client.DeleteAsync($"/api/v1/clients/{created.Id}/phones/{phone.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var secondRemove = await _client.DeleteAsync($"/api/v1/clients/{created.Id}/phones/{phone.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondRemove.StatusCode);
    }

    [Fact]
    public async Task DeleteClient_ThenRestore_ReturnsToActiveFlow()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(100000007),
            fullName = "Cliente Restore"
        }, TestApiClient.JsonOptions);
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(created);

        var del = await _client.DeleteAsync($"/api/v1/clients/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var getDeleted = await _client.GetAsync($"/api/v1/clients/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getDeleted.StatusCode);

        var restore = await _client.PostAsync($"/api/v1/clients/{created.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getRestored = await _client.GetAsync($"/api/v1/clients/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getRestored.StatusCode);
    }

    [Fact]
    public async Task GetAll_NoFilters_UsesDefaultEnvelope()
    {
        var page = await GetClientsPageAsync("/api/v1/clients");
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.True(page.Total >= page.Data.Count);
    }

    [Fact]
    public async Task GetAll_WithPagination_ReturnsCorrectSubset()
    {
        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/clients", new
            {
                type = "PF",
                cpf = GenerateCpf(800000100 + i),
                fullName = $"Pag Cliente {i:D2}"
            }, TestApiClient.JsonOptions);
        }

        var first = await GetClientsPageAsync("/api/v1/clients?q=Pag%20Cliente&page=1&pageSize=2");
        var second = await GetClientsPageAsync("/api/v1/clients?q=Pag%20Cliente&page=2&pageSize=2");
        var third = await GetClientsPageAsync("/api/v1/clients?q=Pag%20Cliente&page=3&pageSize=2");

        Assert.Equal(2, first.Data.Count);
        Assert.Equal(2, second.Data.Count);
        Assert.Single(third.Data);
        Assert.Equal(5, first.Total);
        Assert.Equal(5, second.Total);
        Assert.Equal(5, third.Total);

        var ids = first.Data.Concat(second.Data).Concat(third.Data).Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnFullName()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000200),
            fullName = "Maria Aparecida Silva"
        }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();

        var page = await GetClientsPageAsync("/api/v1/clients?q=Aparecida&pageSize=100");
        Assert.Contains(page.Data, c => c.FullName == "Maria Aparecida Silva");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnCorporateName()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PJ",
            cnpj = GenerateCnpj(80100200),
            corporateName = "Fabrica Souza Tecnologia LTDA"
        }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();

        var page = await GetClientsPageAsync("/api/v1/clients?q=Souza&pageSize=100");
        Assert.Contains(page.Data, c => c.CorporateName == "Fabrica Souza Tecnologia LTDA");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnCpf()
    {
        var cpf = GenerateCpf(800000201);
        var resp = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf,
            fullName = "Cliente CPF Filtro"
        }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();

        var page = await GetClientsPageAsync($"/api/v1/clients?q={cpf[..6]}&pageSize=100");
        Assert.Contains(page.Data, c => c.Cpf == cpf);
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnCnpj()
    {
        var cnpj = GenerateCnpj(80100201);
        var resp = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PJ",
            cnpj,
            corporateName = "Empresa CNPJ Filtro LTDA"
        }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();

        var page = await GetClientsPageAsync($"/api/v1/clients?q={cnpj[..6]}&pageSize=100");
        Assert.Contains(page.Data, c => c.Cnpj == cnpj);
    }

    [Fact]
    public async Task GetAll_WithSearchQ_IsCaseInsensitive()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000202),
            fullName = "Roberto Oliveira"
        }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();

        var lower = await GetClientsPageAsync("/api/v1/clients?q=roberto&pageSize=100");
        var upper = await GetClientsPageAsync("/api/v1/clients?q=ROBERTO&pageSize=100");
        var mixed = await GetClientsPageAsync("/api/v1/clients?q=RoBeRtO&pageSize=100");

        Assert.Contains(lower.Data, c => c.FullName == "Roberto Oliveira");
        Assert.Contains(upper.Data, c => c.FullName == "Roberto Oliveira");
        Assert.Contains(mixed.Data, c => c.FullName == "Roberto Oliveira");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_EscapesUnderscoreLiteral()
    {
        // Sem o escape, "_" viraria wildcard ILIKE e casaria qualquer caractere unico no lugar.
        var resp1 = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000203),
            fullName = "Foo_Lit_Bar"
        }, TestApiClient.JsonOptions);
        resp1.EnsureSuccessStatusCode();
        var resp2 = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000204),
            fullName = "FooXLitXBar"
        }, TestApiClient.JsonOptions);
        resp2.EnsureSuccessStatusCode();

        var page = await GetClientsPageAsync("/api/v1/clients?q=_Lit_&pageSize=100");
        Assert.Contains(page.Data, c => c.FullName == "Foo_Lit_Bar");
        Assert.DoesNotContain(page.Data, c => c.FullName == "FooXLitXBar");
    }

    [Fact]
    public async Task GetAll_WithTypePf_FiltersOnlyPfClients()
    {
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000300),
            fullName = "Cliente TypeFilter PF"
        }, TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PJ",
            cnpj = GenerateCnpj(80100300),
            corporateName = "Empresa TypeFilter PJ LTDA"
        }, TestApiClient.JsonOptions);

        var page = await GetClientsPageAsync("/api/v1/clients?type=PF&pageSize=100");
        Assert.All(page.Data, c => Assert.Equal("PF", c.Type));
    }

    [Fact]
    public async Task GetAll_WithTypePj_FiltersOnlyPjClients()
    {
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000301),
            fullName = "Cliente TypeFilter PF2"
        }, TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PJ",
            cnpj = GenerateCnpj(80100301),
            corporateName = "Empresa TypeFilter PJ2 LTDA"
        }, TestApiClient.JsonOptions);

        var page = await GetClientsPageAsync("/api/v1/clients?type=PJ&pageSize=100");
        Assert.All(page.Data, c => Assert.Equal("PJ", c.Type));
    }

    [Fact]
    public async Task GetAll_WithTypeInvalid_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/clients?type=XX");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithActiveTrue_ReturnsOnlyActive()
    {
        var activeResp = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000400),
            fullName = "Active Cliente"
        }, TestApiClient.JsonOptions);
        activeResp.EnsureSuccessStatusCode();
        var activeDto = await activeResp.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(activeDto);

        var deletedCreate = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000401),
            fullName = "Soft Deleted Cliente"
        }, TestApiClient.JsonOptions);
        deletedCreate.EnsureSuccessStatusCode();
        var deletedDto = await deletedCreate.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(deletedDto);
        await _client.DeleteAsync($"/api/v1/clients/{deletedDto.Id}");

        var page = await GetClientsPageAsync("/api/v1/clients?active=true&pageSize=100");
        Assert.Contains(page.Data, c => c.Id == activeDto.Id);
        Assert.DoesNotContain(page.Data, c => c.Id == deletedDto.Id);
    }

    [Fact]
    public async Task GetAll_WithActiveFalse_ReturnsOnlySoftDeleted()
    {
        var activeResp = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000500),
            fullName = "Stay Active Cliente"
        }, TestApiClient.JsonOptions);
        activeResp.EnsureSuccessStatusCode();
        var activeDto = await activeResp.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(activeDto);

        var deletedCreate = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000501),
            fullName = "Will Be Soft Deleted"
        }, TestApiClient.JsonOptions);
        deletedCreate.EnsureSuccessStatusCode();
        var deletedDto = await deletedCreate.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(deletedDto);
        await _client.DeleteAsync($"/api/v1/clients/{deletedDto.Id}");

        var page = await GetClientsPageAsync("/api/v1/clients?active=false&pageSize=100");
        Assert.Contains(page.Data, c => c.Id == deletedDto.Id);
        Assert.DoesNotContain(page.Data, c => c.Id == activeDto.Id);
    }

    [Fact]
    public async Task GetAll_WithIncludeDeletedTrue_ReturnsActiveAndSoftDeleted()
    {
        var activeResp = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000600),
            fullName = "Visible Active Cliente"
        }, TestApiClient.JsonOptions);
        activeResp.EnsureSuccessStatusCode();
        var activeDto = await activeResp.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(activeDto);

        var deletedCreate = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000601),
            fullName = "Visible Deleted Cliente"
        }, TestApiClient.JsonOptions);
        deletedCreate.EnsureSuccessStatusCode();
        var deletedDto = await deletedCreate.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(deletedDto);
        await _client.DeleteAsync($"/api/v1/clients/{deletedDto.Id}");

        var page = await GetClientsPageAsync("/api/v1/clients?includeDeleted=true&pageSize=100");
        Assert.Contains(page.Data, c => c.Id == activeDto.Id && c.DeletedAt == null);
        Assert.Contains(page.Data, c => c.Id == deletedDto.Id && c.DeletedAt != null);
    }

    [Fact]
    public async Task GetAll_WithActiveAndIncludeDeleted_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/clients?active=true&includeDeleted=true");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageSizeAboveLimit_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/clients?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageSizeZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/clients?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/clients?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_PageBeyondTotal_ReturnsEmptyDataAnd200()
    {
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000700),
            fullName = "Beyond Cliente Unico"
        }, TestApiClient.JsonOptions);

        var resp = await _client.GetAsync("/api/v1/clients?q=Beyond%20Cliente%20Unico&page=99&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<PagedClientsDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page.Data);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task GetAll_OrderedByCreatedAt_Descending()
    {
        var first = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000800),
            fullName = "Order Cliente AAA"
        }, TestApiClient.JsonOptions);
        first.EnsureSuccessStatusCode();
        var firstDto = await first.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(firstDto);

        await Task.Delay(20);

        var second = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000801),
            fullName = "Order Cliente BBB"
        }, TestApiClient.JsonOptions);
        second.EnsureSuccessStatusCode();
        var secondDto = await second.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(secondDto);

        var page = await GetClientsPageAsync("/api/v1/clients?q=Order%20Cliente&pageSize=100");
        var ordered = page.Data
            .Where(c => c.FullName != null && c.FullName.StartsWith("Order Cliente", StringComparison.Ordinal))
            .Select(c => c.Id)
            .ToList();
        Assert.Equal(secondDto.Id, ordered[0]);
        Assert.Equal(firstDto.Id, ordered[1]);
    }

    [Fact]
    public async Task GetAll_HydratesUserIdsEmailsMobilesAndPhones_FromBatchLookup()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800000900),
            fullName = "Hidratacao Cliente"
        }, TestApiClient.JsonOptions);
        create.EnsureSuccessStatusCode();
        var dto = await create.Content.ReadFromJsonAsync<ClientDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.PostAsJsonAsync($"/api/v1/clients/{dto.Id}/emails",
            new { email = "hidratacao1@example.com" }, TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync($"/api/v1/clients/{dto.Id}/mobiles",
            new { number = "+5518999990001" }, TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync($"/api/v1/clients/{dto.Id}/phones",
            new { number = "+5518333330001" }, TestApiClient.JsonOptions);

        var page = await GetClientsPageAsync($"/api/v1/clients?q=Hidratacao%20Cliente&pageSize=100");
        var entry = Assert.Single(page.Data, c => c.Id == dto.Id);
        Assert.Single(entry.ExtraEmails);
        Assert.Equal("hidratacao1@example.com", entry.ExtraEmails[0].Email);
        Assert.Single(entry.MobilePhones);
        Assert.Equal("+5518999990001", entry.MobilePhones[0].Number);
        Assert.Single(entry.LandlinePhones);
        Assert.Equal("+5518333330001", entry.LandlinePhones[0].Number);
    }

    [Fact]
    public async Task GetAll_TotalReflectsFilters_BeforePagination()
    {
        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/clients", new
            {
                type = "PF",
                cpf = GenerateCpf(800001000 + i),
                fullName = $"FiltroTot Cliente {i}"
            }, TestApiClient.JsonOptions);
        }
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800001050),
            fullName = "Outro Nome"
        }, TestApiClient.JsonOptions);

        var page = await GetClientsPageAsync("/api/v1/clients?q=FiltroTot&page=1&pageSize=2");
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Data.Count);
    }

    [Fact]
    public async Task GetAll_CombinedFilters_ApplyTogether()
    {
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(800001100),
            fullName = "Combo Maria"
        }, TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PJ",
            cnpj = GenerateCnpj(80101100),
            corporateName = "Combo Maria LTDA"
        }, TestApiClient.JsonOptions);

        var page = await GetClientsPageAsync("/api/v1/clients?q=Combo%20Maria&type=PF&pageSize=100");
        Assert.All(page.Data, c => Assert.Equal("PF", c.Type));
        Assert.Contains(page.Data, c => c.FullName == "Combo Maria");
        Assert.DoesNotContain(page.Data, c => c.CorporateName == "Combo Maria LTDA");
    }

    private async Task<PagedClientsDto> GetClientsPageAsync(string url)
    {
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<PagedClientsDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        return page;
    }

    private sealed record PagedClientsDto(
        List<ClientDto> Data,
        int Page,
        int PageSize,
        int Total);

    private sealed record ClientDto(
        Guid Id,
        string Type,
        string? Cpf,
        string? FullName,
        string? Cnpj,
        string? CorporateName,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt,
        List<Guid> UserIds,
        List<ClientEmailDto> ExtraEmails,
        List<ClientPhoneDto> MobilePhones,
        List<ClientPhoneDto> LandlinePhones);
    private sealed record ClientEmailDto(Guid Id, string Email, DateTime CreatedAt);
    private sealed record ClientPhoneDto(Guid Id, string Number, DateTime CreatedAt);

    private static string GenerateCpf(int baseDigits)
    {
        var nine = (baseDigits % 1_000_000_000).ToString("D9");
        var d1 = CheckDigit(nine, 10);
        var d2 = CheckDigit(nine + d1, 11);
        return nine + d1 + d2;
    }

    private static int CheckDigit(string input, int startWeight)
    {
        var sum = 0;
        for (var i = 0; i < input.Length; i++)
            sum += (input[i] - '0') * (startWeight - i);
        var result = (sum * 10) % 11;
        return result == 10 ? 0 : result;
    }

    private static string GenerateCnpj(int baseDigits)
    {
        var twelve = (baseDigits % 100_000_000).ToString("D8") + "0001";
        var d1 = CheckDigitCnpj(twelve, CnpjWeights1);
        var d2 = CheckDigitCnpj(twelve + d1, CnpjWeights2);
        return twelve + d1 + d2;
    }

    private static int CheckDigitCnpj(string input, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < input.Length; i++)
            sum += (input[i] - '0') * weights[i];
        var mod = sum % 11;
        return mod < 2 ? 0 : 11 - mod;
    }
}
