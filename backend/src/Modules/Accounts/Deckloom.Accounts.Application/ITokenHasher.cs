using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public interface ITokenHasher
{
    TokenHash Hash(string token);
}
