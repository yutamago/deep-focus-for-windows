namespace DeepFocusForWindows.Services;

public interface IStartupService
{
    bool IsStartOnBootEnabled();
    void SetStartOnBoot(bool enable);
}
