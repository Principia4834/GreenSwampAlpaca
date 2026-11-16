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

namespace GreenSwamp.Alpaca.Server.Pages.TelescopeView;

public partial class TelescopeView : IAsyncDisposable
{
    [Parameter] public int DeviceNumber { get; set; }

    /// <summary>Optional ?model=Reflector etc. (see <see cref="Model3DType"/>); defaults to the standard model.</summary>
    [SupplyParameterFromQuery(Name = "model")] public string? ModelName { get; set; }

    [Inject] private IBrowserSettingsService BrowserSettings { get; set; } = null!;

    private IJSObjectReference? _module;
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
                var pose = Model3D.GetPose(s.AlignmentMode, s.MountType, s.PolarMode, latitude, _axisX, _axisY);
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
        var requestedModelType = GetRequestedModelType();
        Model3DType modelType;
        if (requestedModelType.HasValue)
        {
            modelType = requestedModelType.Value;
        }
        else if (_implicitModelTypeContextKey == selectionKey)
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

        await _module.InvokeAsync<bool>("init", "telescopeCanvas", new
        {
            camera = settings.Camera,
            primaryColor = settings.ModelColor,
            background = settings.Background,
            grid = settings.ShowGrid
        });

        _activeModelType = modelType;
        _settingsStorageKey = settingsKey;
        _selectionStorageKey = selectionKey;
        _loadedMountKey = null;
    }

    private Model3DType? GetRequestedModelType()
        => Enum.TryParse<Model3DType>(ModelName, true, out var modelType)
           && Enum.IsDefined(typeof(Model3DType), modelType)
            ? modelType
            : null;

    private async Task EnsureModelLoadedAsync(TelescopeStateModel s, double latitude)
    {
        if (_module is null) return;

        var mountKey = $"{s.AlignmentMode}|{s.MountType}|{s.PolarMode}|{Math.Round(latitude, 2)}|{_activeModelType}";
        if (mountKey == _loadedMountKey) return;
        _loadedMountKey = mountKey;

        var url = ResolveUrl(Nav.BaseUri.TrimEnd('/'), Model3D.GetModelUrl(_activeModelType, s.AlignmentMode, s.PolarMode));
        await _module.InvokeAsync<string>("loadModel", url, new
        {
            mode = (int)s.AlignmentMode,
            latitude,
            northCompass = Model3D.UseNorthCompass(s.AlignmentMode, latitude)
        });
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
        if (_module is null || _savingView) return;

        _savingView = true;
        try
        {
            var settings = await _module.InvokeAsync<TelescopeViewerSettings?>("getViewerSettings");
            if (settings is null || _settingsStorageKey is null || _selectionStorageKey is null) return;

            settings.ModelType = _activeModelType;
            settings.Normalize();

            await BrowserSettings.SetAsync(_settingsStorageKey, settings);
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
