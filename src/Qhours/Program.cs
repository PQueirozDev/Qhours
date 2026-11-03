using System.Diagnostics;

namespace Qhours;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Qhours-SingleInstance", out var first);
        ApplicationConfiguration.Initialize();
        if (!first)
        {
            Dialogs.Info(null, "Qhours", "O Qhours já está aberto. Procure o ícone na bandeja do sistema, perto do relógio.");
            return;
        }

        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        Application.Run(new MainForm());
    }
}
