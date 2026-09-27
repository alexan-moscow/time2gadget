using TimerGadget.Models;

namespace TimerGadget.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
