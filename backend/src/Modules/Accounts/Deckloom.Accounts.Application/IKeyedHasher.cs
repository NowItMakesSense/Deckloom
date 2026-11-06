namespace Deckloom.Accounts.Application;

public interface IKeyedHasher
{
    byte[] Hash(string value);
}
