using Time2Gadget.Models;

namespace Time2Gadget.Services;

public interface ISettingsService
{
    /// <summary>При последнем Load файла настроек не было — первый запуск на этом компьютере/профиле.</summary>
    bool IsFirstRun { get; }

    AppSettings Load();
    void Save(AppSettings settings);
}
