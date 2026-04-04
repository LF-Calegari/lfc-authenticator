using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static AuthService.Helpers.DbExceptionHelper;

namespace AuthService.Controllers.Clients;

[ApiController]
[Route("clients")]
public class ClientsController : ControllerBase
{
    private const string ClientWithCpfAlreadyExistsMessage = "Já existe cliente com este CPF.";
    private const string ClientWithCnpjAlreadyExistsMessage = "Já existe cliente com este CNPJ.";
    private const string ClientNotFoundMessage = "Cliente não encontrado.";
    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly Regex PhoneRegex = new(
        @"^\+[1-9]\d{11,14}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private readonly AppDbContext _db;

    public ClientsController(AppDbContext db)
    {
        _db = db;
    }

    public class CreateClientRequest
    {
        [Required]
        [MaxLength(2)]
        public string Type { get; set; } = string.Empty;

        [MaxLength(11)]
        public string? Cpf { get; set; }

        [MaxLength(140)]
        public string? FullName { get; set; }

        [MaxLength(14)]
        public string? Cnpj { get; set; }

        [MaxLength(180)]
        public string? CorporateName { get; set; }
    }

    public class UpdateClientRequest : CreateClientRequest;

    public sealed class AddEmailRequest
    {
        [Required]
        [MaxLength(320)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public sealed class AddPhoneRequest
    {
        [Required]
        [MaxLength(20)]
        public string Number { get; set; } = string.Empty;
    }

    public sealed record ClientEmailResponse(Guid Id, string Email, DateTime CreatedAt);
    public sealed record ClientPhoneResponse(Guid Id, string Number, DateTime CreatedAt);
    public sealed record ClientResponse(
        Guid Id,
        string Type,
        string? Cpf,
        string? FullName,
        string? Cnpj,
        string? CorporateName,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt,
        IReadOnlyList<Guid> UserIds,
        IReadOnlyList<ClientEmailResponse> ExtraEmails,
        IReadOnlyList<ClientPhoneResponse> MobilePhones,
        IReadOnlyList<ClientPhoneResponse> LandlinePhones);

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.ClientsCreate)]
    public async Task<IActionResult> Create([FromBody] CreateClientRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var normalized = NormalizeRequest(request.Type, request.Cpf, request.FullName, request.Cnpj, request.CorporateName);
        if (!ValidateClientByType(normalized.Type, normalized.Cpf, normalized.FullName, normalized.Cnpj, normalized.CorporateName))
            return ValidationProblem(ModelState);

        if (normalized.Cpf is not null && await _db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Cpf == normalized.Cpf))
            return Conflict(new { message = ClientWithCpfAlreadyExistsMessage });

        if (normalized.Cnpj is not null && await _db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Cnpj == normalized.Cnpj))
            return Conflict(new { message = ClientWithCnpjAlreadyExistsMessage });

        var now = DateTime.UtcNow;
        var entity = new Client
        {
            Type = normalized.Type,
            Cpf = normalized.Cpf,
            FullName = normalized.FullName,
            Cnpj = normalized.Cnpj,
            CorporateName = normalized.CorporateName,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.Clients.Add(entity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            if (normalized.Cpf is not null)
                return Conflict(new { message = ClientWithCpfAlreadyExistsMessage });

            if (normalized.Cnpj is not null)
                return Conflict(new { message = ClientWithCnpjAlreadyExistsMessage });

            throw;
        }

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, await BuildResponseAsync(entity));
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.ClientsRead)]
    public async Task<IActionResult> GetAll()
    {
        var clients = await _db.Clients
            .AsNoTracking()
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var result = new List<ClientResponse>(clients.Count);
        foreach (var client in clients)
            result.Add(await BuildResponseAsync(client));

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.ClientsRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (client is null)
            return NotFound(new { message = ClientNotFoundMessage });

        return Ok(await BuildResponseAsync(client));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.ClientsUpdate)]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateClientRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client is null)
            return NotFound(new { message = ClientNotFoundMessage });

        var normalized = NormalizeRequest(request.Type, request.Cpf, request.FullName, request.Cnpj, request.CorporateName);
        if (!string.Equals(client.Type, normalized.Type, StringComparison.Ordinal))
            return BadRequest(new { message = "Tipo do cliente não pode ser alterado após a criação." });

        if (!ValidateClientByType(normalized.Type, normalized.Cpf, normalized.FullName, normalized.Cnpj, normalized.CorporateName))
            return ValidationProblem(ModelState);

        if (normalized.Cpf is not null &&
            await _db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Id != id && c.Cpf == normalized.Cpf))
            return Conflict(new { message = ClientWithCpfAlreadyExistsMessage });

        if (normalized.Cnpj is not null &&
            await _db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Id != id && c.Cnpj == normalized.Cnpj))
            return Conflict(new { message = ClientWithCnpjAlreadyExistsMessage });

        client.Cpf = normalized.Cpf;
        client.FullName = normalized.FullName;
        client.Cnpj = normalized.Cnpj;
        client.CorporateName = normalized.CorporateName;
        client.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            if (normalized.Cpf is not null)
                return Conflict(new { message = ClientWithCpfAlreadyExistsMessage });

            if (normalized.Cnpj is not null)
                return Conflict(new { message = ClientWithCnpjAlreadyExistsMessage });

            throw;
        }

        return Ok(await BuildResponseAsync(client));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.ClientsDelete)]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client is null)
            return NotFound(new { message = ClientNotFoundMessage });

        client.DeletedAt = DateTime.UtcNow;
        client.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionPolicies.ClientsRestore)]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var client = await _db.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt != null);
        if (client is null)
            return NotFound(new { message = "Cliente não encontrado ou não está deletado." });

        client.DeletedAt = null;
        client.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Cliente restaurado com sucesso." });
    }

    [HttpPost("{id:guid}/emails")]
    [Authorize(Policy = PermissionPolicies.ClientsUpdate)]
    public async Task<IActionResult> AddEmail(Guid id, [FromBody] AddEmailRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client is null)
            return NotFound(new { message = ClientNotFoundMessage });

        var email = request.Email.Trim().ToLowerInvariant();
        if (!EmailValidator.IsValid(email))
            return BadRequest(new { message = "Email extra inválido." });

        var count = await _db.ClientEmails.CountAsync(e => e.ClientId == id);
        if (count >= 3)
            return BadRequest(new { message = "Limite de 3 emails extras por cliente." });

        var isUsername = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email);
        if (isUsername)
            return Conflict(new { message = "Este email está sendo usado como username e não pode ser email extra." });

        if (await _db.ClientEmails.AnyAsync(e => e.ClientId == id && e.Email == email))
            return Conflict(new { message = "Email extra já cadastrado para este cliente." });

        var entity = new ClientEmail
        {
            ClientId = id,
            Email = email,
            CreatedAt = DateTime.UtcNow
        };
        _db.ClientEmails.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(new ClientEmailResponse(entity.Id, entity.Email, entity.CreatedAt));
    }

    [HttpDelete("{id:guid}/emails/{emailId:guid}")]
    [Authorize(Policy = PermissionPolicies.ClientsUpdate)]
    public async Task<IActionResult> RemoveEmail(Guid id, Guid emailId)
    {
        var email = await _db.ClientEmails.FirstOrDefaultAsync(e => e.ClientId == id && e.Id == emailId);
        if (email is null)
            return NotFound(new { message = "Email extra não encontrado para este cliente." });

        var isUsername = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email.Email);
        if (isUsername)
            return BadRequest(new { message = "Não é permitido remover email que esteja sendo usado como username." });

        _db.ClientEmails.Remove(email);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/mobiles")]
    [Authorize(Policy = PermissionPolicies.ClientsUpdate)]
    public Task<IActionResult> AddMobile(Guid id, [FromBody] AddPhoneRequest request) =>
        AddPhoneInternal(id, request, "mobile", "Limite de 3 celulares por cliente.");

    [HttpDelete("{id:guid}/mobiles/{phoneId:guid}")]
    [Authorize(Policy = PermissionPolicies.ClientsUpdate)]
    public Task<IActionResult> RemoveMobile(Guid id, Guid phoneId) =>
        RemovePhoneInternal(id, phoneId, "mobile");

    [HttpPost("{id:guid}/phones")]
    [Authorize(Policy = PermissionPolicies.ClientsUpdate)]
    public Task<IActionResult> AddLandline(Guid id, [FromBody] AddPhoneRequest request) =>
        AddPhoneInternal(id, request, "phone", "Limite de 3 telefones por cliente.");

    [HttpDelete("{id:guid}/phones/{phoneId:guid}")]
    [Authorize(Policy = PermissionPolicies.ClientsUpdate)]
    public Task<IActionResult> RemoveLandline(Guid id, Guid phoneId) =>
        RemovePhoneInternal(id, phoneId, "phone");

    private async Task<IActionResult> AddPhoneInternal(Guid clientId, AddPhoneRequest request, string type, string limitMessage)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null)
            return NotFound(new { message = ClientNotFoundMessage });

        var normalizedNumber = request.Number.Trim();
        if (!PhoneRegex.IsMatch(normalizedNumber))
            return BadRequest(new { message = "Telefone inválido. Use o formato internacional com DDI e DDD, ex.: +5518981789845." });

        var count = await _db.ClientPhones.CountAsync(p => p.ClientId == clientId && p.Type == type);
        if (count >= 3)
            return BadRequest(new { message = limitMessage });

        if (await _db.ClientPhones.AnyAsync(p => p.ClientId == clientId && p.Type == type && p.Number == normalizedNumber))
            return Conflict(new { message = "Contato já cadastrado para este cliente." });

        var phone = new ClientPhone
        {
            ClientId = clientId,
            Type = type,
            Number = normalizedNumber,
            CreatedAt = DateTime.UtcNow
        };
        _db.ClientPhones.Add(phone);
        await _db.SaveChangesAsync();

        return Ok(new ClientPhoneResponse(phone.Id, phone.Number, phone.CreatedAt));
    }

    private async Task<IActionResult> RemovePhoneInternal(Guid clientId, Guid phoneId, string type)
    {
        var phone = await _db.ClientPhones.FirstOrDefaultAsync(p => p.ClientId == clientId && p.Id == phoneId && p.Type == type);
        if (phone is null)
            return NotFound(new { message = "Contato não encontrado para este cliente." });

        _db.ClientPhones.Remove(phone);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ClientResponse> BuildResponseAsync(Client client)
    {
        var userIds = await _db.Users.AsNoTracking()
            .Where(u => u.ClientId == client.Id)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Id)
            .ToListAsync();

        var emails = await _db.ClientEmails.AsNoTracking()
            .Where(e => e.ClientId == client.Id)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new ClientEmailResponse(e.Id, e.Email, e.CreatedAt))
            .ToListAsync();

        var mobiles = await _db.ClientPhones.AsNoTracking()
            .Where(p => p.ClientId == client.Id && p.Type == "mobile")
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ClientPhoneResponse(p.Id, p.Number, p.CreatedAt))
            .ToListAsync();

        var phones = await _db.ClientPhones.AsNoTracking()
            .Where(p => p.ClientId == client.Id && p.Type == "phone")
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ClientPhoneResponse(p.Id, p.Number, p.CreatedAt))
            .ToListAsync();

        return new ClientResponse(
            client.Id,
            client.Type,
            client.Cpf,
            client.FullName,
            client.Cnpj,
            client.CorporateName,
            client.CreatedAt,
            client.UpdatedAt,
            client.DeletedAt,
            userIds,
            emails,
            mobiles,
            phones);
    }

    private static (string Type, string? Cpf, string? FullName, string? Cnpj, string? CorporateName) NormalizeRequest(
        string type,
        string? cpf,
        string? fullName,
        string? cnpj,
        string? corporateName)
    {
        return (
            type.Trim().ToUpperInvariant(),
            NormalizeDigits(cpf),
            NormalizeText(fullName),
            NormalizeDigits(cnpj),
            NormalizeText(corporateName)
        );
    }

    private bool ValidateClientByType(string type, string? cpf, string? fullName, string? cnpj, string? corporateName)
    {
        if (type != "PF" && type != "PJ")
            ModelState.AddModelError(nameof(CreateClientRequest.Type), "Type deve ser PF ou PJ.");

        if (type == "PF")
            ValidatePfClient(cpf, fullName, cnpj, corporateName);
        else if (type == "PJ")
            ValidatePjClient(cpf, fullName, cnpj, corporateName);

        return ModelState.IsValid;
    }

    private void ValidatePfClient(string? cpf, string? fullName, string? cnpj, string? corporateName)
    {
        if (cpf is null || !IsValidCpf(cpf))
            ModelState.AddModelError(nameof(CreateClientRequest.Cpf), "CPF inválido para cliente PF.");
        if (string.IsNullOrWhiteSpace(fullName))
            ModelState.AddModelError(nameof(CreateClientRequest.FullName), "FullName é obrigatório para cliente PF.");
        if (cnpj is not null)
            ModelState.AddModelError(nameof(CreateClientRequest.Cnpj), "CNPJ não deve ser informado para cliente PF.");
        if (!string.IsNullOrWhiteSpace(corporateName))
            ModelState.AddModelError(nameof(CreateClientRequest.CorporateName),
                "CorporateName não deve ser informado para cliente PF.");
    }

    private void ValidatePjClient(string? cpf, string? fullName, string? cnpj, string? corporateName)
    {
        if (cnpj is null || !IsValidCnpj(cnpj))
            ModelState.AddModelError(nameof(CreateClientRequest.Cnpj), "CNPJ inválido para cliente PJ.");
        if (string.IsNullOrWhiteSpace(corporateName))
            ModelState.AddModelError(nameof(CreateClientRequest.CorporateName),
                "CorporateName é obrigatório para cliente PJ.");
        if (cpf is not null)
            ModelState.AddModelError(nameof(CreateClientRequest.Cpf), "CPF não deve ser informado para cliente PJ.");
        if (!string.IsNullOrWhiteSpace(fullName))
            ModelState.AddModelError(nameof(CreateClientRequest.FullName), "FullName não deve ser informado para cliente PJ.");
    }

    private static string? NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var chars = value.Where(char.IsDigit).ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }

    private static bool IsValidCpf(string cpf)
    {
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1)
            return false;

        var d1 = CheckDigit(cpf[..9], 10);
        var d2 = CheckDigit(cpf[..9] + d1, 11);
        return cpf[9] - '0' == d1 && cpf[10] - '0' == d2;
    }

    private static bool IsValidCnpj(string cnpj)
    {
        if (cnpj.Length != 14 || cnpj.Distinct().Count() == 1)
            return false;

        var w1 = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var w2 = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var d1 = CheckDigit(cnpj[..12], w1);
        var d2 = CheckDigit(cnpj[..12] + d1, w2);
        return cnpj[12] - '0' == d1 && cnpj[13] - '0' == d2;
    }

    private static int CheckDigit(string input, int startWeight)
    {
        var sum = 0;
        for (var i = 0; i < input.Length; i++)
            sum += (input[i] - '0') * (startWeight - i);
        var mod = sum % 11;
        return mod < 2 ? 0 : 11 - mod;
    }

    private static int CheckDigit(string input, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < input.Length; i++)
            sum += (input[i] - '0') * weights[i];
        var mod = sum % 11;
        return mod < 2 ? 0 : 11 - mod;
    }
}
