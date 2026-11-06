namespace Deckloom.Accounts.Infrastructure;

// T-073 (REQ-028): texto puro em pt-BR (sem HTML, nada a escapar); o link e o unico valor dinamico.
internal static class PasswordResetEmailTemplate
{
    public const string Subject = "Deckloom: redefinição de senha";

    private const string Intro =
        "Olá,\n\n" +
        "Recebemos um pedido para redefinir a senha da sua conta no Deckloom.\n" +
        "Para escolher uma nova senha, acesse o link abaixo:\n\n";

    private const string Outro =
        "\n\nSe você não fez esse pedido, ignore esta mensagem: sua senha continua a mesma.\n";

    public static string RenderBody(string resetLink) => Intro + resetLink + Outro;
}
