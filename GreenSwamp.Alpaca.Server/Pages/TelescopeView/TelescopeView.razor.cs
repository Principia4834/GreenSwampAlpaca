/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */


using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Pages.TelescopeView;

public partial class TelescopeView : IAsyncDisposable
{
    [Parameter] public int DeviceNumber { get; set; }

    [Inject] private IBrowserSettingsService BrowserSettings { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

    private IJSObjectReference? _module;
    private TelescopeViewerSettings? _settings;
    private double _axisX;
    private double _axisY;
    private string? _settingsStorageKey;
    private string? _selectionStorageKey;
    private string? _implicitModelTypeContextKey;
    private Model3DType _implicitModelType = Model3DType.Default;
    private Model3DType _activeModelType = Model3DType.Default;
    private bool _savingView;

    private readonly string _viewSessionId = Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _disposeCts = new();
    private PeriodicTimer? _viewHeartbeat;
    private bool _subscribedStateChanged;
    private bool _heartbeatStarted;
    private string? _loadedMountKey;
    private string? _failedMountKey;

    protected override async Task OnParametersSetAsync()
    {
        ActiveViews.Touch(_viewSessionId, DeviceNumber);

        if (_module is not null)
            await PushStateAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            await PushStateAsync();
        }
        finally
        {
            if (!_subscribedStateChanged)
            {
                StateService.StateChanged += OnStateChanged;
                _subscribedStateChanged = true;
            }

            ActiveViews.Touch(_viewSessionId, DeviceNumber);
            if (!_heartbeatStarted)
            {
                _heartbeatStarted = true;
                _ = RunViewHeartbeatAsync();
            }
        }
    }

    // private int _stateChangedCount;

    private void OnStateChanged(object? sender, EventArgs e)
        => _ = InvokeAsync(PushStateAsync);

    private async Task PushStateAsync()
    {
        var s = StateService.GetCurrentState(DeviceNumber);
        _axisX = s.ActualAxisX;
        _axisY = s.ActualAxisY;

        try
        {
            await EnsureViewerInitializedForCurrentContextAsync(s);
            if (_module is null) return;

            var latitude = double.IsFinite(s.SiteLatitude) ? s.SiteLatitude : 0.0;
            await EnsureModelLoadedAsync(s, latitude);

            if (double.IsFinite(_axisX) && double.IsFinite(_axisY))
            {
                var pose = Model3D.GetPose(s.AlignmentMode, s.MountType, s.PolarMode, latitude, _axisX, _axisY,
                    _settings?.RaAdjustmentDegrees ?? 0, _settings?.DecAdjustmentDegrees ?? 0);
                await _module.InvokeVoidAsync("setPose", pose.X, pose.Y, pose.Z, pose.YCentre);
            }

            StateHasChanged();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private async Task EnsureViewerInitializedForCurrentContextAsync(TelescopeStateModel state)
    {
        var selectionKey = TelescopeViewerSettings.GetModelSelectionStorageKey(DeviceNumber, state.MountType);
        Model3DType modelType;
        if (_implicitModelTypeContextKey == selectionKey)
        {
            modelType = _implicitModelType;
        }
        else
        {
            var selection = await BrowserSettings.GetAsync(selectionKey, new TelescopeViewerModelSelection());
            modelType = Enum.IsDefined(typeof(Model3DType), selection.ModelType)
                ? selection.ModelType
                : Model3DType.Default;
            _implicitModelTypeContextKey = selectionKey;
            _implicitModelType = modelType;
        }

        var settingsKey = TelescopeViewerSettings.GetStorageKey(DeviceNumber, state.MountType, modelType);
        if (_settingsStorageKey == settingsKey)
            return;

        if (_module is null)
        {
            await JS.InvokeVoidAsync("ensureBabylonLoaded");
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/telescopeViewer.js");
        }

        if (_settingsStorageKey is not null)
            await _module.InvokeVoidAsync("dispose");

        var settings = await BrowserSettings.GetAsync(settingsKey, TelescopeViewerSettings.CreateDefault(modelType));
        settings.ModelType = modelType;
        settings.Normalize();
        _settings = settings;

        await _module.InvokeAsync<bool>("init", "telescopeCanvas", new
        {
            camera = settings.Camera,
            primaryColor = settings.ModelColor,
            background = settings.Background,
            grid = settings.ShowGrid,
            showCardinals = settings.ShowCardinals,
            cardinalSize = (int)settings.CardinalSize,
            showHeading = settings.ShowHeading,
            useAssembly = settings.UseAssembly,
            assemblySupport = (int)settings.AssemblySupport,
            assemblyMount = (int)settings.AssemblyMount,
            assemblyOta = (int)settings.AssemblyOta,
            assemblyOtaSize = settings.AssemblyOtaSize,
            raAdjustmentDegrees = settings.RaAdjustmentDegrees,
            decAdjustmentDegrees = settings.DecAdjustmentDegrees
        });
        await _module.InvokeVoidAsync("setHeadingElement", "telescopeHeading");

        _activeModelType = modelType;
        _settingsStorageKey = settingsKey;
        _selectionStorageKey = selectionKey;
        _loadedMountKey = null;
        _failedMountKey = null;
    }

    private async Task EnsureModelLoadedAsync(TelescopeStateModel s, double latitude)
    {
        if (_module is null) return;

        var mountKey = $"{s.AlignmentMode}|{s.MountType}|{s.PolarMode}|{Math.Round(latitude, 2)}|{_activeModelType}|{_settings?.UseAssembly}|{_settings?.AssemblySupport}|{_settings?.AssemblyMount}|{_settings?.AssemblyOta}|{_settings?.AssemblyOtaSize}";
        if (mountKey == _loadedMountKey || mountKey == _failedMountKey) return;

        var useAssembly = _settings?.UseAssembly == true;
        var showAssembly = useAssembly && Model3D.ShowsAssembly(s.AlignmentMode, _settings!.AssemblyMount);
        var modelType = useAssembly ? Model3D.GetUsualModel(_settings!.AssemblyOta) : _activeModelType;
        var modelPath = showAssembly
            ? Model3D.GetAssemblyOtaUrl(_settings!.AssemblyOta)
            : Model3D.GetModelUrl(modelType, s.AlignmentMode, s.PolarMode);
        var url = ResolveUrl(Nav.BaseUri.TrimEnd('/'), modelPath);
        var loadResult = await _module.InvokeAsync<string>("loadModel", url, new
        {
            mode = (int)s.AlignmentMode,
            latitude,
            northCompass = Model3D.UseNorthCompass(s.AlignmentMode, latitude),
            assemblyUnavailable = useAssembly && !showAssembly,
            assemblyMount = _settings?.AssemblyMount.ToString(),
            assembly = showAssembly ? new
            {
                support = _settings!.AssemblySupport.ToString().ToLowerInvariant(),
                mount = _settings.AssemblyMount.ToString().ToLowerInvariant(),
                ota = _settings.AssemblyOta.ToString(),
                otaSize = _settings.AssemblyOtaSize
            } : null
        });
        if (loadResult == "Superseded") return;
        if (loadResult?.StartsWith("Error:", StringComparison.Ordinal) == true)
        {
            _failedMountKey = mountKey;
            await JS.InvokeVoidAsync("console.error", $"Telescope viewer model load failed: {loadResult}");
            return;
        }

        _loadedMountKey = mountKey;
        _failedMountKey = null;
    }

    private async Task RunViewHeartbeatAsync()
    {
        _viewHeartbeat ??= new PeriodicTimer(TimeSpan.FromSeconds(3));

        try
        {
            while (await _viewHeartbeat.WaitForNextTickAsync(_disposeCts.Token))
            {
                ActiveViews.Touch(_viewSessionId, DeviceNumber);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
    }

    private async Task SaveViewAsync()
    {
        if (_module is null || _settings is null || _savingView) return;

        _savingView = true;
        try
        {
            var viewerSettings = await _module.InvokeAsync<TelescopeViewerSettings?>("getViewerSettings");
            if (viewerSettings is null || _settingsStorageKey is null || _selectionStorageKey is null) return;

            MergeViewerSettings(viewerSettings);
            _settings.ModelType = _activeModelType;
            _settings.Normalize();

            await BrowserSettings.SetAsync(_settingsStorageKey, _settings);
            await BrowserSettings.SetAsync(_selectionStorageKey,
                new TelescopeViewerModelSelection { ModelType = _activeModelType });
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        finally
        {
            _savingView = false;
        }
    }

    private async Task OpenOptionsAsync()
    {
        if (_module is null || _settings is null) return;

        var viewerSettings = await _module.InvokeAsync<TelescopeViewerSettings?>("getViewerSettings");
        if (viewerSettings is not null)
        {
            MergeViewerSettings(viewerSettings);
        }

        var parameters = new DialogParameters<TelescopeViewerOptionsDialog>
        {
            { dialog => dialog.InitialSettings, _settings }
        };
        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
        var dialog = await DialogService.ShowAsync<TelescopeViewerOptionsDialog>(Loc["TView_OptionsTitle"], parameters, options);
        var result = await dialog.Result;
        if (result is not { Canceled: false, Data: TelescopeViewerSettings updatedSettings }) return;

        updatedSettings.Normalize();
        var state = StateService.GetCurrentState(DeviceNumber);
        var settingsKey = TelescopeViewerSettings.GetStorageKey(DeviceNumber, state.MountType, updatedSettings.ModelType);
        var selectionKey = TelescopeViewerSettings.GetModelSelectionStorageKey(DeviceNumber, state.MountType);
        await BrowserSettings.SetAsync(settingsKey, updatedSettings);
        await BrowserSettings.SetAsync(selectionKey,
            new TelescopeViewerModelSelection { ModelType = updatedSettings.ModelType });

        _settings = updatedSettings;
        _settingsStorageKey = null;
        _selectionStorageKey = selectionKey;
        _implicitModelTypeContextKey = selectionKey;
        _implicitModelType = updatedSettings.ModelType;
        _loadedMountKey = null;
        _failedMountKey = null;

        await _module.InvokeVoidAsync("dispose");
        await PushStateAsync();
    }

    private void MergeViewerSettings(TelescopeViewerSettings viewerSettings)
    {
        if (_settings is null) return;

        _settings.Camera = viewerSettings.Camera;
        _settings.ModelColor = viewerSettings.ModelColor;
        _settings.Background = viewerSettings.Background;
        _settings.ShowGrid = viewerSettings.ShowGrid;
        _settings.ShowCardinals = viewerSettings.ShowCardinals;
        _settings.CardinalSize = viewerSettings.CardinalSize;
        _settings.ShowHeading = viewerSettings.ShowHeading;
        _settings.UseAssembly = viewerSettings.UseAssembly;
        _settings.AssemblySupport = viewerSettings.AssemblySupport;
        _settings.AssemblyMount = viewerSettings.AssemblyMount;
        _settings.AssemblyOta = viewerSettings.AssemblyOta;
        _settings.AssemblyOtaSize = viewerSettings.AssemblyOtaSize;
        _settings.RaAdjustmentDegrees = viewerSettings.RaAdjustmentDegrees;
        _settings.DecAdjustmentDegrees = viewerSettings.DecAdjustmentDegrees;
    }

    private async Task CenterOnMountAsync()
    {
        if (_module is null) return;

        try
        {
            await _module.InvokeVoidAsync("centerOnMount");
            await SaveViewAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private async Task ResetCameraAsync()
    {
        if (_module is null) return;

        try
        {
            await _module.InvokeVoidAsync("resetCamera");
            await SaveViewAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposeCts.IsCancellationRequested)
        {
            _disposeCts.Cancel();
        }

        _viewHeartbeat?.Dispose();
        ActiveViews.Remove(_viewSessionId);

        if (_subscribedStateChanged)
        {
            StateService.StateChanged -= OnStateChanged;
        }

        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }

        _disposeCts.Dispose();
    }

    private static string? ResolveUrl(string baseUri, string? relativePath)
        => relativePath is null ? null : $"{baseUri}/{relativePath.TrimStart('/')}";
}
