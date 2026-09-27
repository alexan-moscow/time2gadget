using Time2Gadget.Models;

namespace Time2Gadget.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
