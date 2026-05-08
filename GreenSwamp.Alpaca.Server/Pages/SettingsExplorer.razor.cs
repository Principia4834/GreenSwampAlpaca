using GreenSwamp.Alpaca.Server.Components;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Services;
using MudBlazor;
using System.Text.Json;
// Alias required: MonitorSettings page class exists in this namespace and shadows the model.
using MonitorSettingsModel = GreenSwamp.Alpaca.Settings.Models.MonitorSettings;
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

    // ── UI state ────────────────────────────────────────────────────────────
    private bool    _saving;
    private bool    _showHidden      = false;
    private string  _feedback        = string.Empty;
    private Severity _feedbackSeverity = Severity.Info;
    private bool    _anyDirty        => Flatten(_treeItems).Any(n => n.IsDirty);

    // ── Hidden-group definitions ────────────────────────────────────────────
    private static readonly HashSet<string> _allHiddenGroups = new(StringComparer.Ordinal)
    {
        "Optics",
        "PEC / PPEC",
        "Hand Controller",
        "GPS"
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
        ["Observatory"]       = "Physical observatory site properties — latitude, longitude, elevation and UTC offset.",
        ["Server Configuration"] = "Alpaca HTTP server behaviour, network binding, authentication and identity.",
        ["Monitor / Logging"] = "Logging filter settings — device, category and message type filters.",
        ["Telescope Devices"] = "Per-device telescope mount settings.",

        // Server Config group descriptions
        ["Network"]           = "TCP port, remote access and Alpaca UDP discovery settings.",
        ["Alpaca Behaviour"]  = "Strict mode, remote disconnects and image-bytes download options.",
        ["Identity & UI"]     = "Location label, browser auto-start and Swagger UI options.",
        ["Authentication"]    = "HTTP Basic authentication settings (username only — use Server Settings to change the password).",

        // Observatory group
        ["Observatory Settings"] = "Latitude, longitude, elevation and UTC offset for the observatory site.",

        // Monitor groups
        ["Device Filters"]       = "Enable or disable log entries by device type (server, telescope, UI).",
        ["Category Filters"]     = "Enable or disable log entries by category (driver, interface, mount, etc.).",
        ["Message Type Filters"] = "Enable or disable log entries by message type (info, warning, error, debug).",
        ["Logging Options"]      = "File logging targets — monitor log, session log and charting data.",

        // Device leaf groups
        ["Device Identity"]      = "Device number, name and enabled state.",
        ["Serial Connection"]    = "Serial port, baud rate, handshake and timeout settings.",
        ["Location"]             = "Latitude, longitude, elevation and UTC offset for this device.",
        ["Optics"]               = "Aperture diameter, aperture area and focal length.",
        ["Environmental"]        = "Atmospheric refraction correction and temperature.",
        ["Coordinate System"]    = "Equatorial coordinate type (J2000 / JNow).",
        ["Tracking"]             = "Tracking rates (sidereal, lunar, solar, king) and RA tracking offset.",
        ["Custom Gearing"]       = "Custom motor gearing step counts and worm-wheel tooth counts.",
        ["Backlash"]             = "RA and Dec backlash compensation in motor steps.",
        ["Pulse Guiding"]        = "Minimum pulse durations, ST4 guide rate and guide rate offsets.",
        ["Sync Limits"]          = "Sync angle limit and no-sync-past-meridian flag.",
        ["PEC / PPEC"]           = "Periodic error correction and predictive PEC settings.",
        ["Encoders"]             = "Enable or disable absolute encoder feedback.",
        ["Hand Controller"]      = "Hand controller speed, mode, flip and anti-backlash settings.",
        ["GPS"]                  = "GPS serial port and baud rate for time/location synchronisation.",
        ["Performance & Display"]= "Display refresh interval, slew rate, GOTO precision and trace logging.",
        ["Home Position"]        = "Stored home axis positions (X/Y) and auto-home axis positions.",
        ["Park Positions"]       = "Named park position list and active park selection.",
        ["Axis / Slew Limits"]   = "Upper/lower axis limits and maximum slew rate.",
        ["Meridian / Hour Angle Limit"] = "Hour angle tracking limit and no-sync-past-meridian for GEM/Polar mounts.",
        ["Horizontal Axis Limit"]= "Horizontal axis tracking limit for AltAz mounts.",
        ["Pier Side"]            = "Pier-side flip settings (GEM mounts).",
        ["Alignment Mode"]       = "Mount alignment mode (AltAz, GermanPolar, Polar).",
    };

    // ── Lifecycle ──────────────────────────────────────────────────────────
    protected override async Task OnInitializedAsync()
    {
        SettingsService.DeviceSettingsChanged  += OnDeviceSettingsChanged;
        SettingsService.MonitorSettingsChanged += OnMonitorSettingsChanged;
        await LoadSettingsAsync();
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

    // ── Tree construction ──────────────────────────────────────────────────
    private void BuildTree()
    {
        var root = new List<SettingsNode>();

        // Observatory
        root.Add(new SettingsNode
        {
            Label    = "Observatory",
            Icon     = Icons.Material.Filled.LocationOn,
            Description = NodeDescriptions["Observatory"],
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.Observatory,
            Children =
            [
                Leaf("Observatory Settings", Icons.Material.Filled.Terrain,
                     SettingsNodeSource.Observatory, "Observatory Settings"),
            ]
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

        // Monitor / Logging
        root.Add(new SettingsNode
        {
            Label    = "Monitor / Logging",
            Icon     = Icons.Material.Filled.ListAlt,
            Description = NodeDescriptions["Monitor / Logging"],
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.Monitor,
            Children =
            [
                Leaf("Device Filters",       Icons.Material.Filled.Devices,         SettingsNodeSource.Monitor, "Device Filters"),
                Leaf("Category Filters",     Icons.Material.Filled.Category,         SettingsNodeSource.Monitor, "Category Filters"),
                Leaf("Message Type Filters", Icons.Material.Filled.FilterList,       SettingsNodeSource.Monitor, "Message Type Filters"),
                Leaf("Logging Options",      Icons.Material.Filled.FolderOpen,       SettingsNodeSource.Monitor, "Logging Options"),
            ]
        });

        // Telescope Devices
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

        _treeItems = root.Select(n => (ITreeItemData<SettingsNode>)new SettingsTreeItemData(n)).ToList();
    }

    private List<SettingsNode> BuildDeviceNodes()
    {
        var nodes = new List<SettingsNode>();
        foreach (var (deviceNumber, device) in _deviceWork)
        {
            var alignmentMode = device.AlignmentMode ?? "GermanPolar";
            var isAltAz       = alignmentMode.Equals("AltAz", StringComparison.OrdinalIgnoreCase);
            var isGem         = !isAltAz;

            var deviceLeaves = new List<SettingsNode>
            {
                DeviceLeaf(deviceNumber, "Device Identity",    Icons.Material.Filled.Badge,           "Device Identity"),
                DeviceLeaf(deviceNumber, "Serial Connection",  Icons.Material.Filled.Cable,           "Serial Connection"),
                DeviceLeaf(deviceNumber, "Location",           Icons.Material.Filled.MyLocation,       "Location"),
            };

            if (IsGroupVisible("Optics"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Optics", Icons.Material.Filled.PhotoCamera, "Optics"));

            deviceLeaves.AddRange(new[]
            {
                DeviceLeaf(deviceNumber, "Environmental",      Icons.Material.Filled.Air,              "Environmental"),
                DeviceLeaf(deviceNumber, "Coordinate System",  Icons.Material.Filled.Explore,          "Coordinate System"),
                DeviceLeaf(deviceNumber, "Tracking",           Icons.Material.Filled.Speed,            "Tracking"),
                DeviceLeaf(deviceNumber, "Custom Gearing",     Icons.Material.Filled.Settings,         "Custom Gearing"),
                DeviceLeaf(deviceNumber, "Backlash",           Icons.Material.Filled.Compress,         "Backlash"),
                DeviceLeaf(deviceNumber, "Pulse Guiding",      Icons.Material.Filled.NearMe,           "Pulse Guiding"),
                DeviceLeaf(deviceNumber, "Sync Limits",           Icons.Material.Filled.SyncAlt,          "Sync Limits"),
                DeviceLeaf(deviceNumber, "Encoders",              Icons.Material.Filled.Numbers,          "Encoders"),
                DeviceLeaf(deviceNumber, "Performance & Display", Icons.Material.Filled.DisplaySettings, "Performance & Display"),
                DeviceLeaf(deviceNumber, "Home Position",         Icons.Material.Filled.Home,             "Home Position"),
                DeviceLeaf(deviceNumber, "Park Positions",        Icons.Material.Filled.LocalParking,     "Park Positions"),
                DeviceLeaf(deviceNumber, "Axis / Slew Limits",    Icons.Material.Filled.Block,            "Axis / Slew Limits"),
            });

            if (IsGroupVisible("PEC / PPEC"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "PEC / PPEC", Icons.Material.Filled.Loop, "PEC / PPEC"));
            if (IsGroupVisible("Hand Controller"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Hand Controller", Icons.Material.Filled.VideogameAsset, "Hand Controller"));
            if (IsGroupVisible("GPS"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "GPS", Icons.Material.Filled.GpsFixed, "GPS"));

            // Mount-type conditional leaves (Q5)
            if (isGem)
            {
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Meridian / Hour Angle Limit",
                    Icons.Material.Filled.Straighten, "Meridian / Hour Angle Limit"));
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Pier Side",
                    Icons.Material.Filled.SwapHoriz, "Pier Side"));
            }
            if (isAltAz)
            {
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Horizontal Axis Limit",
                    Icons.Material.Filled.Straighten, "Horizontal Axis Limit"));
            }

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
        if (node is null || node.Level != SettingsNodeLevel.Group)
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

    // ── Dirty detection ────────────────────────────────────────────────────
    private void HandleSettingChanged()
    {
        if (_selectedNode is null) return;
        _selectedNode.IsDirty = IsNodeDirty(_selectedNode);
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
                    await OfferLocationPropagationAsync();
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
                    await SettingsService.SaveDeviceSettingsAsync(deviceNumber, _deviceWork[deviceNumber]);
                    _deviceOrigJson[deviceNumber] = Serialize(_deviceWork[deviceNumber]);
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
    private async Task OfferLocationPropagationAsync()
    {
        if (!_deviceWork.Any()) return;

        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.ContentText, "Do you want to update all telescope devices with the new observatory location values (Latitude, Longitude, Elevation, UTCOffset)?" },
            { d => d.ConfirmText, "Update all devices" },
            { d => d.CancelText,  "Observatory only" }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small };
        var dialog   = await DialogService.ShowAsync<ConfirmDialog>("Propagate Location?", parameters, options);
        var result   = await dialog.Result;
        if (result is { Canceled: false })
        {
            foreach (var (num, dev) in _deviceWork)
            {
                dev.Latitude  = _observatoryWork.Latitude;
                dev.Longitude = _observatoryWork.Longitude;
                dev.Elevation = _observatoryWork.Elevation;
                dev.UTCOffset = _observatoryWork.UTCOffset;
                await SettingsService.SaveDeviceSettingsAsync(num, dev);
                _deviceOrigJson[num] = Serialize(dev);
                foreach (var n in Flatten(_treeItems).Where(n => n.Source == SettingsNodeSource.Device && n.DeviceNumber == num))
                    n.IsDirty = false;
            }
            ShowSuccess("Observatory location propagated to all devices.");
        }
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
        if (_deviceWork.ContainsKey(updated.DeviceNumber))
            _deviceWork[updated.DeviceNumber] = Clone(updated);
        InvokeAsync(StateHasChanged);
    }

    private void OnMonitorSettingsChanged(object? sender, MonitorSettingsModel updated)
    {
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

    private void ShowSuccess(string msg) { _feedback = msg; _feedbackSeverity = Severity.Success; }
    private void ShowError(string msg)   { _feedback = msg; _feedbackSeverity = Severity.Error;   }

    public void Dispose()
    {
        SettingsService.DeviceSettingsChanged  -= OnDeviceSettingsChanged;
        SettingsService.MonitorSettingsChanged -= OnMonitorSettingsChanged;
    }
}
