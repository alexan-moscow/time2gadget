using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Time2Gadget.Services;

/// <summary>
/// «Запускать с правами администратора» (docs/DECISIONS.md, 2026-09-28; по умолчанию выкл). Нужно, чтобы
/// возвращать на мониторы окна программ, запущенных от администратора: Windows (UIPI) не даёт программе с обычными
/// правами двигать их окна.
/// <para>Без запроса UAC при каждом запуске: при включении один раз (с UAC) создаётся задача Планировщика с
/// «наивысшими правами»; любой запуск с обычными правами (ярлык, автозапуск, перезапуск после обновления) передаёт
/// управление этой задаче и завершается. Аргумент <see cref="ElevatedArg"/> — защита от зацикливания, если задача
/// почему-то запустила программу без повышения.</para>
/// </summary>
public static class ElevationService
{
    public const string TaskName = "Time2Gadget - запуск с правами администратора";
    public const string ElevatedArg = "--elevated-launch";

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static bool LaunchedByTask => Environment.GetCommandLineArgs().Contains(ElevatedArg);

    public static bool TaskExists() => RunSchtasks($"/Query /TN \"{TaskName}\"", elevate: false) == 0;

    /// <summary>
    /// Задача создана для ЭТОЙ копии программы (путь exe запоминается при создании — AppSettings.ElevationTaskExePath).
    /// Портативная и установленная копии делят одни настройки и одну задачу; найдено 2026-09-28: задача от портативной
    /// сборки запускала бы её вместо установленной программы.
    /// </summary>
    public static bool IsTaskForThisCopy(string? taskExePath) =>
        !string.IsNullOrEmpty(taskExePath) && Environment.ProcessPath is { } current
        && string.Equals(System.IO.Path.GetFullPath(taskExePath), System.IO.Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase);

    /// <summary>Запустить программу через задачу (с повышением, без UAC). false — задачи нет/не запустилась.</summary>
    public static bool RunTask() => RunSchtasks($"/Run /TN \"{TaskName}\"", elevate: false) == 0;

    /// <summary>Создать/обновить задачу. Без повышенных прав — через UAC (один раз). false — отказ UAC или ошибка.</summary>
    public static bool CreateTask()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        var xmlPath = Path.Combine(Path.GetTempPath(), "Time2Gadget-elevated-task.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exe), Encoding.Unicode); // schtasks /XML ждёт UTF-16
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", elevate: !IsElevated) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* временный файл — не критично */ }
        }
    }

    /// <summary>Удалить задачу. Сначала без повышения (задача своего пользователя), при отказе — через UAC, если разрешено.</summary>
    public static bool DeleteTask(bool allowUac = true)
    {
        if (!TaskExists()) return true;
        if (RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevate: false) == 0) return true;
        return allowUac && !IsElevated && RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevate: true) == 0;
    }

    private static string BuildTaskXml(string exe)
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name) ?? "";
        string command = SecurityElement.Escape(exe) ?? "";
        string dir = SecurityElement.Escape(Path.GetDirectoryName(exe)) ?? "";
        // Priority 5 — обычный приоритет (по умолчанию задачи идут с пониженным 7); без ограничения времени
        // работы; не останавливать на батарее; повторный /Run при уже запущенной — игнорировать.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Запуск Тайм2гаджета с правами администратора без запроса UAC: нужно, чтобы возвращать на мониторы окна программ, запущенных от администратора. Создаётся и удаляется галочкой в настройках программы.</Description>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{command}"</Command>
                  <Arguments>{ElevatedArg}</Arguments>
                  <WorkingDirectory>{dir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>schtasks.exe без окна; elevate — через UAC («runas»). Отказ в UAC — -1.</summary>
    private static int RunSchtasks(string arguments, bool elevate)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = elevate,
                Verb = elevate ? "runas" : "",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = !elevate,
                RedirectStandardError = !elevate,
            };
            using var p = Process.Start(psi);
            if (p is null) return -1;
            if (!elevate) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); }
            p.WaitForExit(15000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch (Win32Exception)
        {
            return -1; // пользователь отказал в UAC
        }
    }
}
