namespace Deckloom.Accounts.Infrastructure;

public interface IPbkdf2Deriver
{
    byte[] Derive(byte[] password, byte[] salt, int iterations, int length);
}
