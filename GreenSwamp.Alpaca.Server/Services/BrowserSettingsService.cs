using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Services;

public sealed class BrowserSettingsService(IJSRuntime js, ILogger<BrowserSettingsService> logger)
    : IBrowserSettingsService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string JsVersion = Guid.NewGuid().ToString("N");
    private IJSObjectReference? _module;

    public async Task<T> GetAsync<T>(string key, T defaultValue) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(defaultValue);

        try
        {
            var result = await (await GetModuleAsync()).InvokeAsync<StorageReadResult>("getItem", key);
            if (!result.Available)
            {
                logger.LogWarning("Browser storage read failed for {StorageKey}: {Error}", key, result.Error);
                return defaultValue;
            }

            if (result.Value is null)
                return defaultValue;

            try
            {
                return JsonSerializer.Deserialize<T>(result.Value, JsonOptions) ?? defaultValue;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Browser settings value for {StorageKey} is invalid; using defaults", key);
                return defaultValue;
            }
        }
        catch (JSDisconnectedException ex)
        {
            logger.LogWarning(ex, "Browser settings read was interrupted for {StorageKey}; using defaults", key);
            return defaultValue;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Browser settings read was cancelled for {StorageKey}; using defaults", key);
            return defaultValue;
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Browser settings read failed for {StorageKey}; using defaults", key);
            return defaultValue;
        }
    }

    public async Task<bool> SetAsync<T>(string key, T value) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var json = JsonSerializer.Serialize(value, JsonOptions);
        try
        {
            var result = await (await GetModuleAsync()).InvokeAsync<StorageOperationResult>("setItem", key, json);
            if (result.Available)
                return true;

            logger.LogWarning("Browser storage write failed for {StorageKey}: {Error}", key, result.Error);
            return false;
        }
        catch (JSDisconnectedException ex)
        {
            logger.LogWarning(ex, "Browser settings write was interrupted for {StorageKey}", key);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Browser settings write was cancelled for {StorageKey}", key);
            return false;
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Browser settings write failed for {StorageKey}", key);
            return false;
        }
    }

    public async Task<bool> RemoveAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var result = await (await GetModuleAsync()).InvokeAsync<StorageOperationResult>("removeItem", key);
            if (result.Available)
                return true;

            logger.LogWarning("Browser storage removal failed for {StorageKey}: {Error}", key, result.Error);
            return false;
        }
        catch (JSDisconnectedException ex)
        {
            logger.LogWarning(ex, "Browser settings removal was interrupted for {StorageKey}", key);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Browser settings removal was cancelled for {StorageKey}", key);
            return false;
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Browser settings removal failed for {StorageKey}", key);
            return false;
        }
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", $"/js/browserLocalStorage.js?v={JsVersion}");

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
            return;

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The browser releases its module reference when the circuit ends.
        }
    }

    public sealed class StorageReadResult
    {
        [JsonPropertyName("available")] public bool Available { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    public sealed class StorageOperationResult
    {
        [JsonPropertyName("available")] public bool Available { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}