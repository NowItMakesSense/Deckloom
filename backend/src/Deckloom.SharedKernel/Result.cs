using System.Diagnostics.CodeAnalysis;

namespace Deckloom.SharedKernel;

// CA1000 suprimido: fabricas estaticas Result<T>.Success/Failure sao o contrato do T-001 (testes ja dependem).
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Idiomatic Result<T> factory API fixed by T-001.")]
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly IReadOnlyList<DomainError>? _errors;

    private Result(T? value, IReadOnlyList<DomainError>? errors)
    {
        _value = value;
        _errors = errors;
    }

    public bool IsSuccess => _errors is null;

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public DomainError Error => Errors[0];

    public IReadOnlyList<DomainError> Errors => _errors
        ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(DomainError error) => new(default, [error]);

    public static Result<T> Failure(IReadOnlyList<DomainError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors.Count == 0
            ? throw new ArgumentException("A failure needs at least one error.", nameof(errors))
            : new(default, [.. errors]);
    }
}
