using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components
{
    public partial class MountInfo
    {
        [Parameter, EditorRequired] public TelescopeStateModel State { get; set; } = new();
        [Parameter, EditorRequired] public int DeviceNumber { get; set; }
        [Parameter] public bool IsConnected { get; set; }

        /// <summary>Alignment mode passed to the chart pages (SkySettings.AlignmentMode).</summary>
        [Parameter] public string? AlignmentMode { get; set; }

        /// <summary>Device name shown in the chart window label.</summary>
        [Parameter] public string? DeviceLabel { get; set; }

        /// <summary>Diameter of each axis dial in pixels.</summary>
        [Parameter] public int DialSize { get; set; } = 160;

        private enum CoordMode { RaDec, AltAz, Optics }
        private CoordMode _coordMode = CoordMode.RaDec;

        // Referenced by chartWindowInterop.js callbacks; disposed with the component.
        private DotNetObjectReference<MountInfo>? _dotNetRef;

        // -- Plot ------------------------------------------------------------
        private async Task OpenChartWindowAsync(string chartType)
        {
            if (!MountExists()) return;

            var label = string.IsNullOrWhiteSpace(DeviceLabel) ? $"Device {DeviceNumber}" : DeviceLabel;
            var path = $"/charts/{chartType}/{DeviceNumber}" +
                       $"?type={Uri.EscapeDataString(AlignmentMode ?? string.Empty)}" +
                       $"&label={Uri.EscapeDataString(label)}";

            await JS.InvokeVoidAsync(
                "chartWindowInterop.open",
                _dotNetRef ??= DotNetObjectReference.Create(this),
                path,
                $"gs-{chartType}-chart-{DeviceNumber}",
                1200,
                700);
        }

        // -- 3D View ---------------------------------------------------------
        private async Task OpenTelescopeViewAsync()
        {
            if (!MountExists()) return;

            await JS.InvokeVoidAsync(
                "chartWindowInterop.open",
                _dotNetRef ??= DotNetObjectReference.Create(this),
                $"/telescope-view/{DeviceNumber}",
                $"gs-telescope-view-{DeviceNumber}",
                900,
                700);
        }

        [JSInvokable]
        public void OnPopupBlocked(string url)
        {
            Snackbar.Add(
                "Chart window was blocked. Please allow pop-ups for this site.",
                Severity.Warning);
        }

        [JSInvokable]
        public void OnChartWindowClosed(string windowKey)
        {
            // Chart window closed externally — nothing to update.
        }

        private bool MountExists()
        {
            if (MountRegistry.GetInstance(DeviceNumber) != null) return true;
            Snackbar.Add($"Mount device {DeviceNumber} not found", Severity.Error);
            return false;
        }

        public void Dispose() => _dotNetRef?.Dispose();
    }
}
