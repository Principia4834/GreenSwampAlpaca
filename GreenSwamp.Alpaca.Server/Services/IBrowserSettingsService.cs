namespace GreenSwamp.Alpaca.Server.Services;

public interface IBrowserSettingsService
{
    Task<T> GetAsync<T>(string key, T defaultValue) where T : class;

    Task<bool> SetAsync<T>(string key, T value) where T : class;

    Task<bool> RemoveAsync(string key);
}