using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System.Text.Json;
// Alias required: MonitorSettings page class exists in this namespace and shadows the model.
using MonitorSettingsModel = GreenSwamp.Alpaca.Settings.Models.MonitorSettings;
using ObservatoryInfo = GreenSwamp.Alpaca.Settings.Models.ObservatoryInfo;
using ObservatorySettings = GreenSwamp.Alpaca.Settings.Models.ObservatorySettings;
using ServerConfig = GreenSwamp.Alpaca.Settings.Models.ServerConfig;
using SkySettings = GreenSwamp.Alpaca.Settings.Models.SkySettings;

namespace GreenSwamp.Alpaca.Server.Pages;

/// <summary>
/// Adapter so SettingsNode integrates with MudTreeView's ITreeItemData pattern.
/// </summary>
public class SettingsTreeItemData : TreeItemData<SettingsNode>
{
    public SettingsNode Node { get; }

    public SettingsTreeItemData(SettingsNode node) : base(node)
    {
        Node = node;
        Text  = node.Label;
        Icon  = node.Icon;

        if (node.Children.Count > 0)
            Children = node.Children.Select(c => (ITreeItemData<SettingsNode>)new SettingsTreeItemData(c)).ToList();
    }
}

public partial class SettingsExplorer : IDisposable
{
    // ── Working copies ──────────────────────────────────────────────────────
    private ObservatorySettings       _observatoryWork  = new();
    private ServerConfig              _serverConfigWork = new();
    private MonitorSettingsModel      _monitorWork      = new();
    private Dictionary<int, SkySettings> _deviceWork   = new();

    // ── Originals for dirty detection / reset ──────────────────────────────
    private string _observatoryOrigJson  = string.Empty;
    private string _serverConfigOrigJson = string.Empty;
    private string _monitorOrigJson      = string.Empty;
    private Dictionary<int, string> _deviceOrigJson = new();

    // ── Tree state ──────────────────────────────────────────────────────────
    private List<ITreeItemData<SettingsNode>> _treeItems = new();
    private MudTreeView<SettingsNode>? _treeView;
    private SettingsNode? _selectedNode;
    private SettingsNode? _treeSelectedValue;
    private string _searchText = string.Empty;

    // ── Deep-link query parameters ──────────────────────────────────────────
    /// <summary>Device number supplied via ?device=N query parameter.</summary>
    [SupplyParameterFromQuery(Name = "device")]
    public int? DeepLinkDevice { get; set; }

    /// <summary>Group key supplied via ?group=... query parameter.</summary>
    [SupplyParameterFromQuery(Name = "group")]
    public string? DeepLinkGroup { get; set; }

    /// <summary>Ensures deep-link selection fires only once per navigation.</summary>
    private bool _deepLinkApplied;

    // ── UI state ────────────────────────────────────────────────────────────
    private bool    _saving;
    private bool    _deviceManagerBusy = false;
    private bool    _hasPendingDeviceReload = false;
    private bool    _showHidden      = false;
    private bool    _anyDirty        => Flatten(_treeItems).Any(n => n.IsDirty);

    /// <summary>True when the selected node is the Telescope Devices section (device manager card).</summary>
    private static bool IsDeviceManagerNode(SettingsNode? node) =>
        node is { Level: SettingsNodeLevel.Section, Source: SettingsNodeSource.Device };

    /// <summary>True when the selected node is the Monitor/Logging section (shows presets and quick actions card).</summary>
    private static bool IsMonitorLoggingSectionNode(SettingsNode? node) =>
        node is { Level: SettingsNodeLevel.Section, Source: SettingsNodeSource.Monitor, Label: "Logging" };

    /// <summary>True when the selected node is the Observatories section (shows the observatory list card).</summary>
    private static bool IsObservatoriesSectionNode(SettingsNode? node) =>
        node is { Level: SettingsNodeLevel.Section, Source: SettingsNodeSource.Observatory };

    // ── Hidden-group definitions ────────────────────────────────────────────
    private static readonly HashSet<string> _allHiddenGroups = new(StringComparer.Ordinal)
    {
        "Optics",
        "PPEC",
        "Performance & Tuning",
        "Hand Controller"
    };

    private bool IsGroupVisible(string groupKey) => _showHidden || !_allHiddenGroups.Contains(groupKey);

    private void OnShowHiddenChanged(bool value)
    {
        _showHidden = value;
        BuildTree();
    }

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

    // ── Node description lookup ─────────────────────────────────────────────
    private static readonly Dictionary<string, string> NodeDescriptions = new()
    {
        // Section descriptions
        ["Observatories"]     = "Named observatory sites — latitude, longitude, elevation and UTC offset per site.",
        ["Server Configuration"] = "Alpaca HTTP server behaviour, network binding, authentication and identity.",
        ["Logging"] = "Logging filter settings — device, category and message type filters.",
        ["Telescope Devices"] = "Per-device telescope mount settings.",

        // Observatory leaf (shared key for all individual observatory nodes)
        ["Observatory"]       = "Latitude, longitude, elevation and UTC offset for this observatory site.",

        // Server Config group descriptions
        ["Network"]           = "TCP port, remote access and Alpaca UDP discovery settings.",
        ["Alpaca Behaviour"]  = "Strict mode, remote disconnects and image-bytes download options.",
        ["Identity & UI"]     = "Location label, browser auto-start and Swagger UI options.",
        ["Authentication"]    = "HTTP Basic authentication settings (username only — use Server Settings to change the password).",

        // Monitor groups
        ["Configuration Presets"] = "Apply preset configurations for different logging scenarios (Development, Production, Troubleshooting, Profile Debug).",
        ["Device Filters"]       = "Enable or disable log entries by device type (server, telescope, UI).",
        ["Category Filters"]     = "Enable or disable log entries by category (driver, interface, mount, etc.).",
        ["Message Type Filters"] = "Enable or disable log entries by message type (info, warning, error, debug).",
        ["Logging Control"]      = "File logging targets — monitor log, session log and charting data.",

        // Device leaf groups
        ["Device Identity"]      = "Device number, name and enabled state.",
        ["Serial Connection"]    = "Serial port, baud rate, handshake and timeout settings.",
        ["Mount Configuration"]      = "Device identity, serial connection, equatorial coordinates, mount hardware (motor settings, encoders, backlash) and custom gearing.",
        ["Home and Park"]            = "Stored home and park axis positions.",
        ["Limits"]                   = "Axis / slew limits, sync limits, meridian / hour angle limit (GEM/Polar) and horizontal axis limit (AltAz).",
        ["Tracking & Guiding"]       = "Tracking rates and pulse guiding settings.",
        ["Observatory Configuration"] = "Latitude, longitude, elevation, UTC offset, environmental conditions and GPS.",
        ["Optics"]                   = "Aperture diameter, aperture area and focal length.",
        ["Coordinate System"]    = "Equatorial coordinate type (J2000 / JNow).",
        ["Tracking"]             = "Tracking rates (sidereal, lunar, solar, king) and RA tracking offset.",
        ["Custom Gearing"]       = "Custom motor gearing step counts and worm-wheel tooth counts.",
        ["Backlash"]             = "RA and Dec backlash compensation in motor steps.",
        ["Pulse Guiding"]        = "Minimum pulse durations, ST4 guide rate and guide rate offsets.",
        ["Sync Limits"]          = "Sync angle limit and no-sync-past-meridian flag.",
        ["PPEC"]                 = "Predictive PEC settings.",
        ["Encoders"]             = "Enable or disable absolute encoder feedback.",
        ["Hand Controller"]      = "Hand controller speed, mode, flip and anti-backlash settings.",
        ["GPS"]                  = "GPS serial port and baud rate for time/location synchronisation.",
        ["Performance & Tuning"] = "Loop update interval, GOTO precision and trace logging.",
        ["Home Position"]        = "Stored home axis positions (X/Y) and auto-home axis positions.",
        ["Park Positions"]       = "Named park position list and active park selection.",
        ["Axis / Slew Limits"]   = "Upper/lower axis limits and maximum slew rate.",
        ["Horizontal Axis Limit"]= "Horizontal axis tracking limit for AltAz mounts.",
        ["Alignment Mode"]       = "Mount alignment mode (AltAz, GermanPolar, Polar).",
        ["Voice"]               = "Voice announcement settings — enable/disable, voice selection and volume.",
    };

    // ── Lifecycle ──────────────────────────────────────────────────────────
    protected override async Task OnInitializedAsync()
    {
        SettingsService.DeviceSettingsChanged  += OnDeviceSettingsChanged;
        SettingsService.MonitorSettingsChanged += OnMonitorSettingsChanged;
        await LoadSettingsAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (!_deepLinkApplied && (DeepLinkDevice.HasValue || !string.IsNullOrWhiteSpace(DeepLinkGroup)))
            await ApplyDeepLinkAsync();
    }

    private async Task LoadSettingsAsync()
    {
        var obs    = SettingsService.GetObservatorySettings();
        var srv    = SettingsService.GetServerConfig();
        var mon    = SettingsService.GetMonitorSettings();
        // GetAllDeviceSettings returns List<SkySettings>; key by DeviceNumber.
        var devsList = SettingsService.GetAllDeviceSettings();
        var devs     = devsList.ToDictionary(d => d.DeviceNumber);

        _observatoryWork  = Clone(obs);
        _serverConfigWork = Clone(srv);
        _monitorWork      = Clone(mon);
        _deviceWork       = devs.ToDictionary(kv => kv.Key, kv => Clone(kv.Value));

        _observatoryOrigJson  = Serialize(obs);
        _serverConfigOrigJson = Serialize(srv);
        _monitorOrigJson      = Serialize(mon);
        _deviceOrigJson       = devs.ToDictionary(kv => kv.Key, kv => Serialize(kv.Value));

        BuildTree();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Expands the target device branch and selects the requested group leaf
    /// based on the <see cref="DeepLinkDevice"/> and <see cref="DeepLinkGroup"/> query parameters.
    /// </summary>
    private Task ApplyDeepLinkAsync()
    {
        _deepLinkApplied = true;

        // Find the target leaf node.
        var target = Flatten(_treeItems).FirstOrDefault(n =>
            n.Level == SettingsNodeLevel.Group
            && n.Source == SettingsNodeSource.Device
            && (!DeepLinkDevice.HasValue || n.DeviceNumber == DeepLinkDevice.Value)
            && (string.IsNullOrWhiteSpace(DeepLinkGroup)
                || string.Equals(n.GroupKey, DeepLinkGroup, StringComparison.OrdinalIgnoreCase)));

        if (target is null)
            return Task.CompletedTask;

        // The tree has three levels for device leaves:
        //   Level 1 — "Telescope Devices" (top-level section)
        //   Level 2 — "Device N — Name"   (device branch)
        //   Level 3 — "Mount Configuration" etc. (leaf)
        // Both levels 1 and 2 must be expanded so the leaf is visible and
        // receives the primary-colour selection highlight from MudTreeView.
        foreach (var topItem in _treeItems.OfType<SettingsTreeItemData>())
        {
            // Find the device branch ("Device N — Name") within this top-level item.
            var deviceBranch = (topItem.Children ?? [])
                .OfType<SettingsTreeItemData>()
                .FirstOrDefault(b => b.Node.Source == SettingsNodeSource.Device
                                     && b.Node.DeviceNumber == target.DeviceNumber);

            if (deviceBranch is null)
                continue;

            topItem.Expanded    = true;   // expand "Telescope Devices"
            deviceBranch.Expanded = true; // expand "Device N — Name"
            break;
        }

        _selectedNode      = target;
        _treeSelectedValue = target;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ── Tree construction ──────────────────────────────────────────────────
    private void BuildTree()
    {
        var root = new List<SettingsNode>();

        // Telescope Devices (first)
        var deviceSection = new SettingsNode
        {
            Label    = "Telescope Devices",
            Icon     = Icons.Material.Filled.TravelExplore,
            Description = NodeDescriptions["Telescope Devices"],
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.Device,
            Children = BuildDeviceNodes()
        };
        root.Add(deviceSection);

        // Observatories
        var observatorySection = new SettingsNode
        {
            Label    = "Observatories",
            Icon     = Icons.Material.Filled.LocationOn,
            Description = NodeDescriptions["Observatories"],
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.Observatory,
            Children = BuildObservatoryNodes()
        };
        root.Add(observatorySection);

        // Logging — single non-expandable section; rendered by MonitorSettingsCombinedPanel
        root.Add(new SettingsNode
        {
            Label    = "Logging",
            Icon     = Icons.Material.Filled.ListAlt,
            Description = NodeDescriptions["Logging"],
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.Monitor
        });

        // Server Configuration
        root.Add(new SettingsNode
        {
            Label    = "Server Configuration",
            Icon     = Icons.Material.Filled.Dns,
            Description = NodeDescriptions["Server Configuration"],
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.ServerConfig,
            Children =
            [
                Leaf("Network",          Icons.Material.Filled.NetworkCheck,   SettingsNodeSource.ServerConfig, "Network"),
                Leaf("Alpaca Behaviour", Icons.Material.Filled.Tune,           SettingsNodeSource.ServerConfig, "Alpaca Behaviour"),
                Leaf("Identity & UI",    Icons.Material.Filled.Person,         SettingsNodeSource.ServerConfig, "Identity & UI"),
                Leaf("Authentication",   Icons.Material.Filled.Lock,           SettingsNodeSource.ServerConfig, "Authentication"),
            ]
        });

        _treeItems = root.Select(n => (ITreeItemData<SettingsNode>)new SettingsTreeItemData(n)).ToList();
    }

    private List<SettingsNode> BuildDeviceNodes()
    {
        var nodes = new List<SettingsNode>();
        foreach (var (deviceNumber, device) in _deviceWork)
        {
            var alignmentMode = device.AlignmentMode ?? "GermanPolar";
            var isAltAz       = alignmentMode.Equals("AltAz", StringComparison.OrdinalIgnoreCase);

            var deviceLeaves = new List<SettingsNode>
            {
                DeviceLeaf(deviceNumber, "Mount Configuration",       Icons.Material.Filled.Build,          "Mount Configuration"),
                DeviceLeaf(deviceNumber, "Observatory Configuration", Icons.Material.Filled.MyLocation,     "Observatory Configuration"),
            };

            if (IsGroupVisible("Optics"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Optics", Icons.Material.Filled.PhotoCamera, "Optics"));

            deviceLeaves.AddRange(new[]
            {
                DeviceLeaf(deviceNumber, "Tracking & Guiding",   Icons.Material.Filled.Speed,            "Tracking & Guiding"),
                DeviceLeaf(deviceNumber, "Home and Park",        Icons.Material.Filled.Home,             "Home and Park"),
                DeviceLeaf(deviceNumber, "Limits",               Icons.Material.Filled.Block,            "Limits"),
                DeviceLeaf(deviceNumber, "Voice",                Icons.Material.Filled.RecordVoiceOver,  "Voice"),
            });

            if (IsGroupVisible("Performance & Tuning"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Performance & Tuning", Icons.Material.Filled.Loop, "Performance & Tuning"));
            if (IsGroupVisible("PPEC"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "PPEC", Icons.Material.Filled.Loop, "PPEC"));
            if (IsGroupVisible("Hand Controller"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Hand Controller", Icons.Material.Filled.VideogameAsset, "Hand Controller"));

            nodes.Add(new SettingsNode
            {
                Label        = $"Device {deviceNumber} — {device.DeviceName}",
                Icon         = Icons.Material.Filled.DeviceHub,
                Description  = $"Settings for telescope device {deviceNumber}: {device.DeviceName}.",
                Level        = SettingsNodeLevel.Section,
                Source       = SettingsNodeSource.Device,
                DeviceNumber = deviceNumber,
                Children     = deviceLeaves
            });
        }
        return nodes;
    }

    private List<SettingsNode> BuildObservatoryNodes() =>
        _observatoryWork.Observatories.Select(obs => new SettingsNode
        {
            Label         = obs.Name,
            Icon          = Icons.Material.Filled.Terrain,
            Description   = NodeDescriptions.GetValueOrDefault("Observatory", $"Observatory site: {obs.Name}"),
            Level         = SettingsNodeLevel.Group,
            Source        = SettingsNodeSource.Observatory,
            GroupKey      = "Observatory",
            ObservatoryId = obs.Id,
            DeviceNumber  = -1
        }).ToList();

    private static SettingsNode Leaf(string label, string icon,
        SettingsNodeSource source, string groupKey) => new()
    {
        Label       = label,
        Icon        = icon,
        Description = NodeDescriptions.GetValueOrDefault(groupKey, label),
        Level       = SettingsNodeLevel.Group,
        Source      = source,
        GroupKey    = groupKey,
        DeviceNumber = -1
    };

    private static SettingsNode DeviceLeaf(int deviceNumber, string label, string icon, string groupKey) => new()
    {
        Label        = label,
        Icon         = icon,
        Description  = NodeDescriptions.GetValueOrDefault(groupKey, label),
        Level        = SettingsNodeLevel.Group,
        Source       = SettingsNodeSource.Device,
        DeviceNumber = deviceNumber,
        GroupKey     = groupKey
    };

    // ── Node selection with unsaved-changes guard (Q4) ─────────────────────
    private async Task OnTreeNodeSelected(SettingsNode? node)
    {
        if (node is null)
            return;

        var selectable = node.Level == SettingsNodeLevel.Group || IsDeviceManagerNode(node) || IsMonitorLoggingSectionNode(node) || IsObservatoriesSectionNode(node);
        if (!selectable)
            return;

        if (_selectedNode is not null && _selectedNode.IsDirty && _selectedNode != node)
        {
            var confirmed = await ShowDiscardDialogAsync();
            if (!confirmed)
            {
                // Revert tree selection
                _treeSelectedValue = _selectedNode;
                StateHasChanged();
                return;
            }
            ResetGroup(_selectedNode);
        }

        _selectedNode = node;
        StateHasChanged();
    }

    private async Task AddDeviceAsync()
    {
        var dialog = await DialogService.ShowAsync<AddDeviceDialog>(
            "Add Telescope Device",
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });

        var result = await dialog.Result;
        if (result.Canceled || result.Data is not ValueTuple<string, AlignmentMode> add)
            return;

        _deviceManagerBusy = true;
        try
        {
            var (deviceName, alignmentMode) = add;
            var nextDeviceNumber = _deviceWork.Keys.DefaultIfEmpty(-1).Max() + 1;

            await SettingsService.CreateDeviceForModeAsync(nextDeviceNumber, deviceName, alignmentMode);
            await SettingsService.AddAlpacaDeviceAsync(new AlpacaDevice
            {
                DeviceNumber = nextDeviceNumber,
                DeviceName = deviceName,
                DeviceType = "Telescope"
            });

            await LoadSettingsAsync();
            SelectDeviceManagerNode();
            _hasPendingDeviceReload = true;
            ShowSuccess($"Added device {nextDeviceNumber}: {deviceName}.");
        }
        catch (Exception ex)
        {
            ShowError($"Error adding device: {ex.Message}");
        }
        finally
        {
            _deviceManagerBusy = false;
        }
    }

    private async Task DeleteDeviceAsync(int deviceNumber)
    {
        if (!_deviceWork.TryGetValue(deviceNumber, out var device))
        {
            ShowError($"Device {deviceNumber} was not found.");
            return;
        }

        var parameters = new DialogParameters<DeleteDeviceDialog>
        {
            { d => d.DeviceNumber, deviceNumber },
            { d => d.DeviceName, device.DeviceName }
        };

        var dialog = await DialogService.ShowAsync<DeleteDeviceDialog>(
            "Delete Telescope Device",
            parameters,
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });

        var result = await dialog.Result;
        if (result.Canceled)
            return;

        _deviceManagerBusy = true;
        try
        {
            await SettingsService.RemoveAlpacaDeviceAsync(deviceNumber);
            await SettingsService.DeleteDeviceSettingsAsync(deviceNumber);

            await LoadSettingsAsync();
            SelectDeviceManagerNode();
            _hasPendingDeviceReload = true;
            ShowSuccess($"Deleted device {deviceNumber}: {device.DeviceName}.");
        }
        catch (Exception ex)
        {
            ShowError($"Error deleting device {deviceNumber}: {ex.Message}");
        }
        finally
        {
            _deviceManagerBusy = false;
        }
    }

    private void SelectDeviceManagerNode()
    {
        var managerNode = Flatten(_treeItems).FirstOrDefault(IsDeviceManagerNode);
        if (managerNode is null)
            return;

        _selectedNode = managerNode;
        _treeSelectedValue = managerNode;
        StateHasChanged();
    }

    private async Task HotReloadDevicesAsync()
    {
        var connectedDevices = MountRegistry.GetAllInstances()
            .Where(kvp => kvp.Value.IsConnected)
            .Select(kvp => $"Device {kvp.Key} \u2014 {kvp.Value.DeviceName}")
            .ToList();

        var parameters = new DialogParameters<HotReloadDevicesDialog>
        {
            { d => d.ConnectedDevices, connectedDevices }
        };

        var dialog = await DialogService.ShowAsync<HotReloadDevicesDialog>(
            "Apply Device Changes",
            parameters,
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });

        var result = await dialog.Result;
        if (result.Canceled)
            return;

        _deviceManagerBusy = true;
        try
        {
            var reloadResult = await DeviceRegistry.ReloadAllDevicesAsync();
            if (reloadResult.Success)
            {
                _hasPendingDeviceReload = false;
                await LoadSettingsAsync();
                SelectDeviceManagerNode();
                ShowSuccess($"Device registry reloaded \u2014 {reloadResult.ReloadedCount} device(s) active.");
            }
            else
            {
                ShowError($"Hot reload failed: {reloadResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Hot reload error: {ex.Message}");
        }
        finally
        {
            _deviceManagerBusy = false;
        }
    }

    // ── Dirty detection ────────────────────────────────────────────────────
    private void HandleSettingChanged()
    {
        if (_selectedNode is null) return;
        _selectedNode.IsDirty = IsNodeDirty(_selectedNode);
        StateHasChanged();
    }

    /// <summary>
    /// Applies a monitor settings preset to the working copy.
    /// Marks only the Monitor section node as dirty and refreshes the UI.
    /// </summary>
    private async Task ApplyMonitorPresetAsync(GreenSwamp.Alpaca.Settings.Models.MonitorSettings preset)
    {
        _monitorWork = preset;

        // Mark all affected leaf nodes as dirty (presets affect all three filter categories + logging options)
        var affectedGroupKeys = new[] { "Device Filters", "Category Filters", "Message Type Filters", "Logging Control" };
        MarkMonitorLeafNodesDirty(affectedGroupKeys);

        // Also mark the section node as dirty
        var monitorSectionNode = Flatten(_treeItems).FirstOrDefault(n => 
            n.Source == SettingsNodeSource.Monitor && 
            n.Level == SettingsNodeLevel.Section);

        if (monitorSectionNode is not null)
        {
            monitorSectionNode.IsDirty = IsNodeDirty(monitorSectionNode);
        }

        StateHasChanged();

        // Show success feedback
        ShowSuccess("Preset applied. Review the changes and click Save when ready.");
    }

    /// <summary>
    /// Marks Monitor leaf nodes as dirty based on group keys.
    /// Used to track which filter categories have changed.
    /// </summary>
    private void MarkMonitorLeafNodesDirty(string[] affectedGroupKeys)
    {
        foreach (var groupKey in affectedGroupKeys)
        {
            var leafNode = Flatten(_treeItems).FirstOrDefault(n => 
                n.Source == SettingsNodeSource.Monitor && 
                n.Level == SettingsNodeLevel.Group &&
                n.GroupKey == groupKey);

            if (leafNode is not null)
            {
                leafNode.IsDirty = IsNodeDirty(leafNode);
            }
        }
    }

    /// <summary>
    /// Handles quick action button clicks from the Monitor/Logging settings card.
    /// </summary>
    private async Task HandleMonitorQuickActionAsync(string actionName)
    {
        // Determine which leaf nodes are affected by this action
        var affectedGroupKeys = actionName switch
        {
            "SelectAllDevices" => new[] { "Device Filters" },
            "SelectAllCategories" => new[] { "Category Filters" },
            "SelectAllTypes" => new[] { "Message Type Filters" },
            "ClearAllFilters" => new[] { "Device Filters", "Category Filters", "Message Type Filters" },
            _ => Array.Empty<string>()
        };

        switch (actionName)
        {
            case "SelectAllDevices":
                _monitorWork.ServerDevice = true;
                _monitorWork.Telescope = true;
                _monitorWork.Ui = true;
                ShowSuccess("All devices selected");
                break;

            case "SelectAllCategories":
                _monitorWork.Other = true;
                _monitorWork.Driver = true;
                _monitorWork.Interface = true;
                _monitorWork.Server = true;
                _monitorWork.Mount = true;
                _monitorWork.Alignment = true;
                ShowSuccess("All categories selected");
                break;

            case "SelectAllTypes":
                _monitorWork.Information = true;
                _monitorWork.Data = true;
                _monitorWork.Warning = true;
                _monitorWork.Error = true;
                _monitorWork.Debug = true;
                ShowSuccess("All types selected");
                break;

            case "ClearAllFilters":
                _monitorWork.ServerDevice = false;
                _monitorWork.Telescope = false;
                _monitorWork.Ui = false;
                _monitorWork.Other = false;
                _monitorWork.Driver = false;
                _monitorWork.Interface = false;
                _monitorWork.Server = false;
                _monitorWork.Mount = false;
                _monitorWork.Alignment = false;
                _monitorWork.Information = false;
                _monitorWork.Data = false;
                _monitorWork.Warning = false;
                _monitorWork.Error = false;
                _monitorWork.Debug = false;
                ShowSuccess("All filters cleared");
                break;
        }

        // Mark affected leaf nodes and the section node as dirty
        MarkMonitorLeafNodesDirty(affectedGroupKeys);

        var monitorSectionNode = Flatten(_treeItems).FirstOrDefault(n => 
            n.Source == SettingsNodeSource.Monitor && 
            n.Level == SettingsNodeLevel.Section);

        if (monitorSectionNode is not null)
        {
            monitorSectionNode.IsDirty = IsNodeDirty(monitorSectionNode);
        }

        StateHasChanged();
    }

    private bool IsNodeDirty(SettingsNode node) => node.Source switch
    {
        SettingsNodeSource.Observatory  => Serialize(_observatoryWork)  != _observatoryOrigJson,
        SettingsNodeSource.ServerConfig => Serialize(_serverConfigWork) != _serverConfigOrigJson,
        SettingsNodeSource.Monitor      => Serialize(_monitorWork)      != _monitorOrigJson,
        SettingsNodeSource.Device when _deviceWork.TryGetValue(node.DeviceNumber, out var dev)
                                        => Serialize(dev) != _deviceOrigJson.GetValueOrDefault(node.DeviceNumber),
        _ => false
    };

    /// <summary>
    /// Saves the Logging settings from the card's Save button.
    /// </summary>
    private async Task SaveMonitorSettingsAsync()
    {
        _saving = true;
        try
        {
            await SettingsService.SaveMonitorSettingsAsync(_monitorWork);
            _monitorOrigJson = Serialize(_monitorWork);

            // Clear dirty flag on all Monitor nodes (section + all leaves)
            foreach (var n in Flatten(_treeItems).Where(n => n.Source == SettingsNodeSource.Monitor))
            {
                n.IsDirty = false;
            }

            ShowSuccess("Logging settings saved successfully.");
        }
        catch (Exception ex)
        {
            ShowError($"Error saving Logging settings: {ex.Message}");
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>
    /// Resets the Logging settings from the card's Reset button.
    /// Follows the same pattern as ResetGroup - clears dirty flags on all Monitor nodes.
    /// </summary>
    private async Task ResetMonitorSettingsAsync()
    {
        _monitorWork = DeserializeOrDefault<MonitorSettingsModel>(_monitorOrigJson);

        // Clear dirty flag on all Monitor nodes (section + all leaves)
        foreach (var n in Flatten(_treeItems).Where(n => n.Source == SettingsNodeSource.Monitor))
        {
            n.IsDirty = false;
        }

        StateHasChanged();
        ShowSuccess("Logging settings reset to last saved state.");
    }

    // ── Save ───────────────────────────────────────────────────────────────
    private async Task SaveSelectedGroupAsync()
    {
        if (_selectedNode is null) return;
        _saving = true;
        try
        {
            switch (_selectedNode.Source)
            {
                case SettingsNodeSource.Observatory:
                    await SettingsService.SaveObservatorySettingsAsync(_observatoryWork);
                    _observatoryOrigJson = Serialize(_observatoryWork);
                    break;

                case SettingsNodeSource.ServerConfig:
                    await SettingsService.SaveServerConfigAsync(_serverConfigWork);
                    _serverConfigOrigJson = Serialize(_serverConfigWork);
                    break;

                case SettingsNodeSource.Monitor:
                    MonitorSettingsModel monSnapshot = _monitorWork;
                    await SettingsService.SaveMonitorSettingsAsync(monSnapshot);
                    _monitorOrigJson = Serialize(_monitorWork);
                    break;

                case SettingsNodeSource.Device:
                    var deviceNumber = _selectedNode.DeviceNumber;
                    var incoming     = _deviceWork[deviceNumber];

                    // Detect breaking changes by comparing to the persisted original.
                    var original = DeserializeOrDefault<SkySettings>(
                        _deviceOrigJson.GetValueOrDefault(deviceNumber, string.Empty));

                    var breakingChanges = BuildBreakingChangeList(original, incoming);
                    if (breakingChanges.Count > 0 && DeviceRegistry.IsMountRunning(deviceNumber))
                    {
                        var clientCount = DeviceRegistry.GetConnectedClientCount(deviceNumber);
                        bool confirmed  = await ConfirmBreakingChangeAsync(
                            incoming.DeviceName, clientCount, breakingChanges);
                        if (!confirmed)
                        {
                            _saving = false;
                            return;
                        }

                        // Save first so in-memory settings match disk before recreating the instance.
                        await SettingsService.SaveDeviceSettingsAsync(deviceNumber, incoming);
                        _deviceOrigJson[deviceNumber] = Serialize(incoming);
                        // Tear down the old instance and recreate from disk with the new settings.
                        // ASCOM clients must reconnect after this point.
                        await DeviceRegistry.ReplaceDeviceAsync(deviceNumber);
                    }
                    else
                    {
                        // Device is stopped or no breaking change: file write only.
                        await SettingsService.SaveDeviceSettingsAsync(deviceNumber, incoming);
                        _deviceOrigJson[deviceNumber] = Serialize(incoming);
                    }
                    break;
            }

            // Mark all sibling leaves of same source as clean (Q1: whole backing object saved)
            foreach (var node in Flatten(_treeItems).Where(n => n.Source == _selectedNode.Source
                     && (_selectedNode.Source != SettingsNodeSource.Device
                         || n.DeviceNumber == _selectedNode.DeviceNumber)))
            {
                node.IsDirty = false;
            }

            _selectedNode.IsDirty = false;
            ShowSuccess("Settings saved successfully.");
        }
        catch (Exception ex)
        {
            ShowError($"Error saving settings: {ex.Message}");
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>
    /// Compares the original persisted settings to the working copy and returns a
    /// human-readable description for each breaking field that has changed.
    /// Breaking fields: Mount type, COM Port, Baud Rate, Alignment Mode.
    /// </summary>
    private static List<string> BuildBreakingChangeList(SkySettings original, SkySettings incoming)
    {
        var changes = new List<string>();

        if (!string.Equals(original.Mount, incoming.Mount, StringComparison.Ordinal))
            changes.Add($"Mount Type: {original.Mount} → {incoming.Mount}");

        if (!string.Equals(original.Port, incoming.Port, StringComparison.OrdinalIgnoreCase))
            changes.Add($"COM Port: {original.Port} → {incoming.Port}");

        if (original.BaudRate != incoming.BaudRate)
            changes.Add($"Baud Rate: {original.BaudRate} → {incoming.BaudRate}");

        if (!string.Equals(original.AlignmentMode, incoming.AlignmentMode, StringComparison.Ordinal))
            changes.Add($"Alignment Mode: {original.AlignmentMode} → {incoming.AlignmentMode}");

        return changes;
    }

    /// <summary>
    /// Opens <see cref="BreakingSettingsChangeDialog"/> when connected clients exist.
    /// Returns true if the change may proceed (confirmed or no clients connected).
    /// </summary>
    private async Task<bool> ConfirmBreakingChangeAsync(
        string deviceName, int clientCount, List<string> changedFields)
    {
        if (clientCount == 0)
            return true;

        var parameters = new DialogParameters<BreakingSettingsChangeDialog>
        {
            { d => d.DeviceName,    deviceName    },
            { d => d.ClientCount,   clientCount   },
            { d => d.ChangedFields, changedFields }
        };

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small };
        var dialog  = await DialogService.ShowAsync<BreakingSettingsChangeDialog>(
            "Breaking Settings Change", parameters, options);
        var result  = await dialog.Result;

        return result is { Canceled: false };
    }

    // ── Reset ──────────────────────────────────────────────────────────────
    private void ResetSelectedGroup()
    {
        if (_selectedNode is null) return;
        ResetGroup(_selectedNode);
        StateHasChanged();
    }

    private void ResetGroup(SettingsNode node)
    {
        switch (node.Source)
        {
            case SettingsNodeSource.Observatory:
                _observatoryWork = DeserializeOrDefault<ObservatorySettings>(_observatoryOrigJson);
                BuildTree(); // Structural changes (add/delete) are reversed by rebuilding from restored working copy
                break;
            case SettingsNodeSource.ServerConfig:
                _serverConfigWork = DeserializeOrDefault<ServerConfig>(_serverConfigOrigJson);
                break;
            case SettingsNodeSource.Monitor:
                _monitorWork = DeserializeOrDefault<MonitorSettingsModel>(_monitorOrigJson);
                break;
            case SettingsNodeSource.Device:
                if (_deviceOrigJson.TryGetValue(node.DeviceNumber, out var origJson))
                    _deviceWork[node.DeviceNumber] = DeserializeOrDefault<SkySettings>(origJson);
                break;
        }

        // Clear dirty flag on all leaves of same source/device
        foreach (var n in Flatten(_treeItems).Where(n => n.Source == node.Source
                 && (node.Source != SettingsNodeSource.Device || n.DeviceNumber == node.DeviceNumber)))
        {
            n.IsDirty = false;
        }
    }

    // ── Observatory location propagation dialog (Q7) ───────────────────────
    // ── Observatory CRUD ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Presents the Add Observatory dialog, appends the result to the working copy, rebuilds
    /// the tree, and marks all observatory nodes dirty. No data is persisted until the user saves.
    /// </summary>
    private async Task AddObservatoryAsync()
    {
        var existingNames = _observatoryWork.Observatories.Select(o => o.Name).ToList();
        var parameters = new DialogParameters<AddObservatoryDialog>
        {
            { d => d.ExistingNames, existingNames }
        };
        var dialog = await DialogService.ShowAsync<AddObservatoryDialog>(
            "Add Observatory",
            parameters,
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });

        var result = await dialog.Result;
        if (result.Canceled || result.Data is not ObservatoryInfo newObs) return;

        _observatoryWork.Observatories.Add(newObs);
        BuildTree();
        MarkObservatoryNodesDirty();

        // Auto-navigate to the new observatory leaf node
        var newNode = Flatten(_treeItems)
            .FirstOrDefault(n => n.Source == SettingsNodeSource.Observatory
                                 && n.ObservatoryId == newObs.Id);
        if (newNode is not null)
        {
            _selectedNode      = newNode;
            _treeSelectedValue = newNode;
        }
        StateHasChanged();
    }

    /// <summary>
    /// Shows a confirmation dialog and, if confirmed, removes the observatory from the working copy.
    /// Rebuilds the tree and marks observatory nodes dirty. No data is persisted until the user saves.
    /// </summary>
    private async Task DeleteObservatoryAsync(string id)
    {
        if (_observatoryWork.Observatories.Count <= 1) return; // guard — UI disables the button

        var obs = _observatoryWork.Observatories.FirstOrDefault(o => o.Id == id);
        if (obs is null) return;

        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.ContentText, $"Delete observatory \"{obs.Name}\"? This change will not be persisted until you click Save." },
            { d => d.ConfirmText, "Delete" },
            { d => d.CancelText,  "Cancel" }
        };
        var dialog = await DialogService.ShowAsync<ConfirmDialog>(
            "Delete Observatory", parameters,
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small });
        var result = await dialog.Result;
        if (result.Canceled) return;

        _observatoryWork.Observatories.RemoveAll(o => o.Id == id);

        // If the deleted node was selected, clear selection before rebuild
        if (_selectedNode?.ObservatoryId == id)
        {
            _selectedNode      = null;
            _treeSelectedValue = null;
        }

        BuildTree();
        MarkObservatoryNodesDirty();
        SelectObservatoriesSectionNode();
    }

    /// <summary>
    /// Confirms with the user then immediately persists the observatory's Lat/Long/Elevation
    /// to every device settings file. Updates both working copies and JSON baselines so no
    /// device node shows a dirty indicator afterwards.
    /// </summary>
    private async Task HandleCopyObservatoryToAllDevicesAsync(ObservatoryInfo obs)
    {
        if (_deviceWork.Count == 0) return;

        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.ContentText, $"CCopy Latitude, Longitude, Elevation and GPS settings from '{obs.Name}' to all {_deviceWork.Count} telescope device(s) and save immediately?" },
            { d => d.ConfirmText, "Copy & Save" },
            { d => d.CancelText,  "Cancel" }
        };
        var dialog = await DialogService.ShowAsync<ConfirmDialog>(
            "Copy Observatory to All Devices", parameters,
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        if (result.Canceled) return;

        _saving = true;
        try
        {
            foreach (var (deviceNumber, dev) in _deviceWork)
            {
                dev.Latitude    = obs.Latitude;
                dev.Longitude   = obs.Longitude;
                dev.Elevation   = obs.Elevation;
                dev.GpsPort     = obs.GpsPort;
                dev.GpsBaudRate = obs.GpsBaudRate;

                await SettingsService.SaveDeviceSettingsAsync(deviceNumber, dev);

                // Update the JSON baseline so dirty detection sees no change.
                _deviceOrigJson[deviceNumber] = Serialize(dev);
            }

            // Ensure all device nodes are marked clean.
            foreach (var node in Flatten(_treeItems).Where(n => n.Source == SettingsNodeSource.Device))
                node.IsDirty = false;

            ShowSuccess($"Location and GPS settings from '{obs.Name}' saved to {_deviceWork.Count} device(s).");
        }
        catch (Exception ex)
        {
            ShowError($"Error copying observatory to devices: {ex.Message}");
        }
        finally
        {
            _saving = false;
            StateHasChanged();
        }
    }

    private void MarkObservatoryNodesDirty()
    {
        foreach (var node in Flatten(_treeItems).Where(n => n.Source == SettingsNodeSource.Observatory))
            node.IsDirty = IsNodeDirty(node);
    }

    private void SelectObservatoriesSectionNode()
    {
        var node = Flatten(_treeItems).FirstOrDefault(IsObservatoriesSectionNode);
        if (node is null) return;
        _selectedNode      = node;
        _treeSelectedValue = node;
        StateHasChanged();
    }

    // ── Unsaved-changes guard dialog (Q4) ──────────────────────────────────
    private async Task<bool> ShowDiscardDialogAsync()
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.ContentText, "You have unsaved changes. Discard them and switch groups?" },
            { d => d.ConfirmText, "Discard" },
            { d => d.CancelText,  "Stay" }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall };
        var dialog  = await DialogService.ShowAsync<ConfirmDialog>("Unsaved Changes", parameters, options);
        var result  = await dialog.Result;
        return result is { Canceled: false };
    }

    // ── Tree expand / collapse ─────────────────────────────────────────────
    private async Task ExpandAll()
    {
        if (_treeView is not null)
            await _treeView.ExpandAllAsync();
    }

    private async Task CollapseAll()
    {
        if (_treeView is not null)
            await _treeView.CollapseAllAsync();
    }

    // ── Search filter ──────────────────────────────────────────────────────
    private bool MatchesSearch(SettingsNode node)
    {
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        return node.Label.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || node.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    // ── External settings-change callbacks ────────────────────────────────
    private void OnDeviceSettingsChanged(object? sender, SkySettings updated)
    {
        // Skip when we triggered the save ourselves — the working copy is already current
        // and replacing the reference would orphan child-editor components still holding the old one.
        if (_saving) return;
        if (_deviceWork.ContainsKey(updated.DeviceNumber))
            _deviceWork[updated.DeviceNumber] = Clone(updated);
        InvokeAsync(StateHasChanged);
    }

    private void OnMonitorSettingsChanged(object? sender, MonitorSettingsModel updated)
    {
        // Same guard as above.
        if (_saving) return;
        _monitorWork = Clone(updated);
        InvokeAsync(StateHasChanged);
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static string Serialize<T>(T obj) =>
        JsonSerializer.Serialize(obj, _jsonOpts);

    private static T Clone<T>(T obj) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, _jsonOpts), _jsonOpts)!;

    private static T DeserializeOrDefault<T>(string json) where T : new() =>
        string.IsNullOrEmpty(json) ? new T() : JsonSerializer.Deserialize<T>(json, _jsonOpts) ?? new T();

    private static IEnumerable<SettingsNode> Flatten(IEnumerable<ITreeItemData<SettingsNode>> items) =>
        items.SelectMany(i =>
        {
            var node = ((SettingsTreeItemData)i).Node;
            return new[] { node }.Concat(Flatten(i.Children ?? []));
        });

    private void ShowSuccess(string msg) => Snackbar.Add(msg, Severity.Success);
    private void ShowError(string msg)   => Snackbar.Add(msg, Severity.Error);

    public void Dispose()
    {
        SettingsService.DeviceSettingsChanged  -= OnDeviceSettingsChanged;
        SettingsService.MonitorSettingsChanged -= OnMonitorSettingsChanged;
    }
}
