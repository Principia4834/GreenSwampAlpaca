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
using GreenSwamp.Alpaca.Settings.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Pages.TelescopeView;

public partial class TelescopeView : IAsyncDisposable
{
    [Parameter] public int DeviceNumber { get; set; }

    /// <summary>Optional ?model=Reflector etc. (see <see cref="Model3DType"/>); defaults to the standard model.</summary>
    [SupplyParameterFromQuery(Name = "model")] public string? ModelName { get; set; }

    private IJSObjectReference? _module;
    private double _axisX;
    private double _axisY;
    private string _activeModelSetName = string.Empty;
    private bool _savingView;

    private readonly string _viewSessionId = Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _disposeCts = new();
    private PeriodicTimer? _viewHeartbeat;
    private bool _subscribedStateChanged;
    private bool _heartbeatStarted;
    private int? _initializedDeviceNumber;
    private string? _loadedMountKey;

    protected override async Task OnParametersSetAsync()
    {
        ActiveViews.Touch(_viewSessionId, DeviceNumber);

        if (_module is not null && _initializedDeviceNumber != DeviceNumber)
        {
            await EnsureViewerInitializedForCurrentDeviceAsync();
            await PushStateAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await EnsureViewerInitializedForCurrentDeviceAsync();

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

        await PushStateAsync();
    }

    private void OnStateChanged(object? sender, EventArgs e)
        => _ = InvokeAsync(PushStateAsync);

    private async Task PushStateAsync()
    {
        if (_module is null) return;

        var s = StateService.GetCurrentState(DeviceNumber);
        _axisX = s.ActualAxisX;
        _axisY = s.ActualAxisY;

        try
        {
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

    private async Task EnsureViewerInitializedForCurrentDeviceAsync()
    {
        await JS.InvokeVoidAsync("ensureBabylonLoaded");
        _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/telescopeViewer.js");

        if (_initializedDeviceNumber.HasValue && _initializedDeviceNumber.Value != DeviceNumber)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose");
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }

        var modelSet = SettingsService.GetModelSets();
        var activeEntry = modelSet.ModelSets
            .FirstOrDefault(s => s.Name == modelSet.ActiveModelSet)
            ?? new ModelSetEntry { Name = "Prototype", Models = new ModelFiles() };

        _activeModelSetName = activeEntry.Name;

        // Camera angles are degrees. The model is (re)loaded by EnsureModelLoadedAsync on the next state push.
        await _module.InvokeAsync<bool>("init", "telescopeCanvas", new { camera = (object?)activeEntry.Camera });

        _loadedMountKey = null;
        _initializedDeviceNumber = DeviceNumber;
    }

    private async Task EnsureModelLoadedAsync(TelescopeStateModel s, double latitude)
    {
        if (_module is null) return;

        var modelType = Enum.TryParse<Model3DType>(ModelName, true, out var parsed) ? parsed : Model3DType.Default;
        var mountKey = $"{s.AlignmentMode}|{s.MountType}|{s.PolarMode}|{Math.Round(latitude, 2)}|{modelType}";
        if (mountKey == _loadedMountKey) return;
        _loadedMountKey = mountKey;

        var url = ResolveUrl(Nav.BaseUri.TrimEnd('/'), Model3D.GetModelUrl(modelType, s.AlignmentMode, s.PolarMode));
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
            var cameraState = await _module.InvokeAsync<global::GreenSwamp.Alpaca.Settings.Models.CameraState?>("getCameraState");
            if (cameraState is null) return;

            var modelSets = SettingsService.GetModelSets();
            var entry = modelSets.ModelSets.FirstOrDefault(s => s.Name == _activeModelSetName);
            if (entry is null) return;

            entry.Camera = cameraState;
            await SettingsService.SaveModelSetsAsync(modelSets);
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
