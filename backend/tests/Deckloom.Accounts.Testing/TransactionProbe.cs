namespace Deckloom.Accounts.Testing;

public sealed record TransactionSample(string Call, bool TransactionOpen);

// T-137: sonda dos dubles. Cada chamada que nao pode ocorrer com transacao aberta (atraso, hash, Google, e-mail,
// imagem) registra o estado de InMemoryUnitOfWork.IsTransactionOpen; sem Observe, nao registra nada.
public sealed class TransactionProbe
{
    private readonly List<TransactionSample> _samples = [];
    private InMemoryUnitOfWork? _unitOfWork;

    public IReadOnlyList<TransactionSample> Samples => _samples;

    public void Observe(InMemoryUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    public void Record(string call)
    {
        if (_unitOfWork is not null)
        {
            _samples.Add(new TransactionSample(call, _unitOfWork.IsTransactionOpen));
        }
    }
}
