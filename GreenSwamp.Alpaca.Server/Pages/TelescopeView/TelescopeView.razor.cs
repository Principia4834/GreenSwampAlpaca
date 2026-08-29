using GreenSwamp.Alpaca.Server.Services;
using GreenSwamp.Alpaca.Settings.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Pages.TelescopeView;

public partial class TelescopeView : IAsyncDisposable
{
    [Parameter] public int DeviceNumber { get; set; }

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
            await _module.InvokeVoidAsync("setAxes", _axisX, _axisY);
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

        var baseUri = Nav.BaseUri.TrimEnd('/');
        object modelsObj = new
        {
            support = ResolveUrl(baseUri, activeEntry.Models.Support),
            structural = ResolveUrl(baseUri, activeEntry.Models.Structural),
            primaryAxis = ResolveUrl(baseUri, activeEntry.Models.PrimaryAxis),
            secondaryAxis = ResolveUrl(baseUri, activeEntry.Models.SecondaryAxis),
            ota = ResolveUrl(baseUri, activeEntry.Models.Ota),
            otaSecondary = ResolveUrl(baseUri, activeEntry.Models.OtaSecondary),
            counterWeight = ResolveUrl(baseUri, activeEntry.Models.CounterWeight)
        };

        var s = StateService.GetCurrentState(DeviceNumber);
        var effectiveLatitude = s.AlignmentMode == ASCOM.Common.DeviceInterfaces.AlignmentMode.AltAz ? 90.0 : s.SiteLatitude;
        var alignmentModeStr = s.AlignmentMode.ToString();
        var mountTypeStr = s.MountType.ToString();

        await _module.InvokeVoidAsync("init",
            "telescopeCanvas",
            effectiveLatitude,
            alignmentModeStr,
            mountTypeStr,
            modelsObj,
            (object?)activeEntry.Camera);

        _initializedDeviceNumber = DeviceNumber;
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
