using System.Globalization;
using AuthService.Models;

namespace AuthService.Data;

internal static class LegacyClientFactory
{
    public static Client BuildPfClientForUser(User user, HashSet<string> usedCpfs, int ordinal)
    {
        var cpf = GenerateUniqueCpf(usedCpfs, ordinal);
        return new Client
        {
            Type = "PF",
            Cpf = cpf,
            FullName = string.IsNullOrWhiteSpace(user.Name) ? $"Usuário legado {ordinal}" : user.Name.Trim(),
            Cnpj = null,
            CorporateName = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DeletedAt = null
        };
    }

    private static string GenerateUniqueCpf(ISet<string> usedCpfs, int seed)
    {
        var counter = Math.Max(seed, 1);
        while (true)
        {
            var baseDigits = (counter % 1_000_000_000).ToString("D9", CultureInfo.InvariantCulture);
            var d1 = CpfDigit(baseDigits, 10);
            var d2 = CpfDigit(baseDigits + d1, 11);
            var cpf = baseDigits + d1 + d2;
            if (usedCpfs.Add(cpf))
                return cpf;

            counter++;
        }
    }

    private static int CpfDigit(string input, int factorStart)
    {
        var sum = 0;
        for (var i = 0; i < input.Length; i++)
            sum += (input[i] - '0') * (factorStart - i);
        var result = (sum * 10) % 11;
        return result == 10 ? 0 : result;
    }
}
