using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Core.Interfaces;

public interface IUserSettingsService
{
    UserSettings Load();

    void Save(UserSettings settings);
}
