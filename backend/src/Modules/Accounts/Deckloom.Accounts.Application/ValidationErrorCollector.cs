using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

// Agrega erros de validacao na ordem de insercao, preenchendo o Field do contrato da API quando o erro vem sem ele.
internal sealed class ValidationErrorCollector
{
    private readonly List<DomainError> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public IReadOnlyList<DomainError> Errors => _errors;

    public T? Collect<T>(Result<T> result, string? field = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return result.Value;
        }

        foreach (var error in result.Errors)
        {
            _errors.Add(error.Field is null && field is not null ? error with { Field = field } : error);
        }

        return default;
    }

    // Senha DEFINIDA (cadastro e redefinicao): alem do formato, recusa senha da lista local (SEC-05, ASVS 2.1.7).
    // O login usa Password.Create direto e nunca passa por aqui.
    public Password? CollectNewPassword(string? input, string field)
    {
        var password = Collect(Password.Create(input), field);
        if (password is not null && CommonPasswordList.Contains(password.Reveal()))
        {
            _errors.Add(new DomainError(Password.TooCommonCode, field));
            return null;
        }

        return password;
    }
}
