using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using AuthService.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class ClientsApiTests : IAsyncLifetime
{
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
            new { email = IntegrationBootstrapSeeder.Email }, TestApiClient.JsonOptions);
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
                Email = IntegrationBootstrapSeeder.Email,
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

    private sealed record ClientDto(
        Guid Id,
        string Type,
        string? Cpf,
        string? FullName,
        string? Cnpj,
        string? CorporateName);
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
        var d1 = CheckDigitCnpj(twelve, new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        var d2 = CheckDigitCnpj(twelve + d1, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        return twelve + d1 + d2;
    }

    private static int CheckDigitCnpj(string input, IReadOnlyList<int> weights)
    {
        var sum = 0;
        for (var i = 0; i < input.Length; i++)
            sum += (input[i] - '0') * weights[i];
        var mod = sum % 11;
        return mod < 2 ? 0 : 11 - mod;
    }
}
