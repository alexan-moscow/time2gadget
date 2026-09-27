using Velopack;

namespace Time2Gadget;

/// <summary>
/// Точка входа вместо сгенерированного WPF Main (docs/DECISIONS.md, 2026-09-27). Velopack обязан
/// отработать самым первым: при установке/обновлении/удалении установщик запускает exe со служебными
/// аргументами, и VelopackApp обрабатывает их и сразу завершает процесс — окно при этом не открывается.
/// При обычном запуске Run() ничего не делает, и дальше стартует приложение как раньше.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
