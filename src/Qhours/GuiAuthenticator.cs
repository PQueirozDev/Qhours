using SteamKit2.Authentication;

namespace Qhours;

/// <summary>Steam Guard pela interface: código do app, código do e-mail ou aprovação no celular.</summary>
public sealed class GuiAuthenticator(MainForm form) : IAuthenticator
{
    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) =>
        Task.FromResult(Ask((previousCodeWasIncorrect ? "Código incorreto. " : "") +
            "Digite o código do Steam Guard mostrado no app Steam do celular."));

    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) =>
        Task.FromResult(Ask((previousCodeWasIncorrect ? "Código incorreto. " : "") +
            $"Digite o código do Steam Guard enviado para {email}."));

    public Task<bool> AcceptDeviceConfirmationAsync()
    {
        var approve = (bool)form.Invoke(() => Dialogs.Confirm(form, "Steam Guard",
            "Como você quer confirmar este login?\n\nAprove a notificação no app Steam do celular, ou digite o código de 5 letras.",
            "Aprovar no celular", "Digitar código"));
        if (approve) form.PostLog("Aguardando você aprovar o login no app Steam...");
        return Task.FromResult(approve);
    }

    string Ask(string prompt)
    {
        var code = (string?)form.Invoke(() => Dialogs.Prompt(form, "Steam Guard", prompt));
        if (string.IsNullOrWhiteSpace(code)) throw new OperationCanceledException();
        return code.Trim().ToUpperInvariant();
    }
}
