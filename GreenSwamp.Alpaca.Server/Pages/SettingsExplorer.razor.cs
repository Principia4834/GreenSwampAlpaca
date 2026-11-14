using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components;
using GreenSwamp.Alpaca.Server.Components.Dialogs;
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
using MountType = GreenSwamp.Alpaca.Settings.Models.MountType;

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
            Children = [.. node.Children.Select(c => (ITreeItemData<SettingsNode>)new SettingsTreeItemData(c))];
    }
}

public partial class SettingsExplorer : IDisposable
{
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    // -- Working copies ------------------------------------------------------
    private ObservatorySettings       _observatoryWork  = new();
    private ServerConfig              _serverConfigWork = new();
    private MonitorSettingsModel      _monitorWork      = new();
    private Dictionary<int, SkySettings> _deviceWork   = [];

    // -- Originals for dirty detection / reset ------------------------------
    private string _observatoryOrigJson  = string.Empty;
    private string _serverConfigOrigJson = string.Empty;
    private string _monitorOrigJson      = string.Empty;
    private Dictionary<int, string> _deviceOrigJson = [];

    // -- Tree state ----------------------------------------------------------
    private List<ITreeItemData<SettingsNode>> _treeItems = [];
    private MudTreeView<SettingsNode>? _treeView;
    private SettingsNode? _selectedNode;
    private SettingsNode? _treeSelectedValue;
    private string _searchText = string.Empty;

    // -- Deep-link query parameters ------------------------------------------
    /// <summary>Device number supplied via ?device=N query parameter.</summary>
    [SupplyParameterFromQuery(Name = "device")]
    public int? DeepLinkDevice { get; set; }

    /// <summary>Group key supplied via ?group=... query parameter.</summary>
    [SupplyParameterFromQuery(Name = "group")]
    public string? DeepLinkGroup { get; set; }

    /// <summary>Ensures deep-link selection fires only once per navigation.</summary>
    private bool _deepLinkApplied;

    // -- UI state ------------------------------------------------------------
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
        node is { Level: SettingsNodeLevel.Section, Source: SettingsNodeSource.Monitor };

    /// <summary>True when the selected node is the Observatories section (shows the observatory list card).</summary>
    private static bool IsObservatoriesSectionNode(SettingsNode? node) =>
        node is { Level: SettingsNodeLevel.Section, Source: SettingsNodeSource.Observatory };

    // -- Hidden-group definitions --------------------------------------------
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

    // -- Node label / description lookup (values are resource keys) ----------
    private static readonly Dictionary<string, string> NodeDescriptions = new()
    {
        // Section descriptions
        ["Observatories"]     = "SE_Desc_Observatories",
        ["Server Configuration"] = "SE_Desc_ServerConfiguration",
        ["Logging"] = "SE_Desc_Logging",
        ["Telescope Devices"] = "SE_Desc_TelescopeDevices",

        // Observatory leaf (shared key for all individual observatory nodes)
        ["Observatory"]       = "SE_Desc_Observatory",

        // Server Config group descriptions
        ["Alpaca Settings"]   = "SE_Desc_AlpacaSettings",
        ["User Interface"]    = "SE_Desc_UserInterface",
        ["Security"]          = "SE_Desc_Security",

        // Monitor groups
        ["Device Filters"]       = "SE_Desc_DeviceFilters",
        ["Category Filters"]     = "SE_Desc_CategoryFilters",
        ["Message Type Filters"] = "SE_Desc_MessageTypeFilters",
        ["Logging Control"]      = "SE_Desc_LoggingControl",

        // Device leaf groups
        ["Device Identity"]      = "SE_Desc_DeviceIdentity",
        ["Serial Connection"]    = "SE_Desc_SerialConnection",
        ["Mount Configuration"]      = "SE_Desc_MountConfiguration",
        ["Home and Park"]            = "SE_Desc_HomeAndPark",
        ["Limits"]                   = "SE_Desc_Limits",
        ["Tracking & Guiding"]       = "SE_Desc_TrackingAndGuiding",
        ["Observatory Configuration"] = "SE_Desc_ObservatoryConfiguration",
        ["Optics"]                   = "SE_Desc_Optics",
        ["Coordinate System"]    = "SE_Desc_CoordinateSystem",
        ["Tracking"]             = "SE_Desc_Tracking",
        ["Custom Gearing"]       = "SE_Desc_CustomGearing",
        ["Backlash"]             = "SE_Desc_Backlash",
        ["Pulse Guiding"]        = "SE_Desc_PulseGuiding",
        ["Sync Limits"]          = "SE_Desc_SyncLimits",
        ["PPEC"]                 = "SE_Desc_Ppec",
        ["Encoders"]             = "SE_Desc_Encoders",
        ["Hand Controller"]      = "SE_Desc_HandController",
        ["GPS"]                  = "SE_Desc_Gps",
        ["Performance & Tuning"] = "SE_Desc_PerformanceAndTuning",
        ["Home Position"]        = "SE_Desc_HomePosition",
        ["Park Positions"]       = "SE_Desc_ParkPositions",
        ["Axis / Slew Limits"]   = "SE_Desc_AxisSlewLimits",
        ["Horizontal Axis Limit"]= "SE_Desc_HorizontalAxisLimit",
        ["Alignment Mode"]       = "SE_Desc_AlignmentMode",
        ["Voice"]               = "SE_Desc_Voice",
    };

    private static readonly Dictionary<string, string> NodeLabelKeys = new()
    {
        ["Telescope Devices"]         = "SE_Node_TelescopeDevices",
        ["Observatories"]             = "SE_Node_Observatories",
        ["Logging"]                   = "SE_Node_Logging",
        ["Server Configuration"]      = "SE_Node_ServerConfiguration",
        ["Alpaca Settings"]           = "SE_Node_AlpacaSettings",
        ["User Interface"]            = "SE_Node_UserInterface",
        ["Security"]                  = "SE_Node_Security",
        ["Mount Configuration"]       = "SE_Node_MountConfiguration",
        ["Observatory Configuration"] = "SE_Node_ObservatoryConfiguration",
        ["Optics"]                    = "SE_Node_Optics",
        ["Tracking & Guiding"]        = "SE_Node_TrackingAndGuiding",
        ["Home and Park"]             = "SE_Node_HomeAndPark",
        ["Limits"]                    = "SE_Node_Limits",
        ["Voice"]                     = "SE_Node_Voice",
        ["Performance & Tuning"]      = "SE_Node_PerformanceAndTuning",
        ["PPEC"]                      = "SE_Node_Ppec",
        ["Hand Controller"]           = "SE_Node_HandController",
    };

    private string Describe(string groupKey, string fallback) =>
        NodeDescriptions.TryGetValue(groupKey, out var key) ? Loc[key].Value : fallback;

    private string NodeLabel(string label) =>
        NodeLabelKeys.TryGetValue(label, out var key) ? Loc[key].Value : label;

    // -- Lifecycle ----------------------------------------------------------
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

    // -- Tree construction --------------------------------------------------
    private void BuildTree()
    {
        var root = new List<SettingsNode>();

        // Telescope Devices (first)
        var deviceSection = new SettingsNode
        {
            Label    = Loc["SE_Node_TelescopeDevices"].Value,
            Icon     = Icons.Material.Filled.TravelExplore,
            Description = Describe("Telescope Devices", string.Empty),
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.Device,
            Children = BuildDeviceNodes()
        };
        root.Add(deviceSection);

        // Observatories
        var observatorySection = new SettingsNode
        {
            Label    = Loc["SE_Node_Observatories"].Value,
            Icon     = Icons.Material.Filled.LocationOn,
            Description = Describe("Observatories", string.Empty),
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.Observatory,
            Children = BuildObservatoryNodes()
        };
        root.Add(observatorySection);

        // Logging — single non-expandable section; rendered by MonitorSettingsCombinedPanel
        root.Add(new SettingsNode
        {
            Label    = Loc["SE_Node_Logging"].Value,
            Icon     = Icons.Material.Filled.ListAlt,
            Description = Describe("Logging", string.Empty),
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.Monitor
        });

        // Server Configuration
        root.Add(new SettingsNode
        {
            Label    = Loc["SE_Node_ServerConfiguration"].Value,
            Icon     = Icons.Material.Filled.Dns,
            Description = Describe("Server Configuration", string.Empty),
            Level    = SettingsNodeLevel.Section,
            Source   = SettingsNodeSource.ServerConfig,
            Children =
            [
                Leaf("Alpaca Settings", Icons.Material.Filled.NetworkCheck,     SettingsNodeSource.ServerConfig, "Alpaca Settings"),
                Leaf("User Interface",   Icons.Material.Filled.DisplaySettings, SettingsNodeSource.ServerConfig, "User Interface"),
                Leaf("Security",         Icons.Material.Filled.Lock,            SettingsNodeSource.ServerConfig, "Security"),
            ]
        });

        _treeItems = [.. root.Select(n => (ITreeItemData<SettingsNode>)new SettingsTreeItemData(n))];
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

            deviceLeaves.AddRange(
            [
                DeviceLeaf(deviceNumber, "Tracking & Guiding",   Icons.Material.Filled.Speed,            "Tracking & Guiding"),
                DeviceLeaf(deviceNumber, "Home and Park",        Icons.Material.Filled.Home,             "Home and Park"),
                DeviceLeaf(deviceNumber, "Limits",               Icons.Material.Filled.Block,            "Limits"),
                DeviceLeaf(deviceNumber, "Voice",                Icons.Material.Filled.RecordVoiceOver,  "Voice"),
            ]);

            if (IsGroupVisible("Performance & Tuning"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Performance & Tuning", Icons.Material.Filled.Loop, "Performance & Tuning"));
            if (IsGroupVisible("PPEC"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "PPEC", Icons.Material.Filled.Loop, "PPEC"));
            if (IsGroupVisible("Hand Controller"))
                deviceLeaves.Add(DeviceLeaf(deviceNumber, "Hand Controller", Icons.Material.Filled.VideogameAsset, "Hand Controller"));

            nodes.Add(new SettingsNode
            {
                Label        = Loc["SE_DeviceLabel", deviceNumber, device.DeviceName].Value,
                Icon         = Icons.Material.Filled.DeviceHub,
                Description  = Loc["SE_DeviceDescription", deviceNumber, device.DeviceName].Value,
                Level        = SettingsNodeLevel.Section,
                Source       = SettingsNodeSource.Device,
                DeviceNumber = deviceNumber,
                Children     = deviceLeaves
            });
        }
        return nodes;
    }

    private List<SettingsNode> BuildObservatoryNodes() =>
        [.. _observatoryWork.Observatories.Select(obs => new SettingsNode
        {
            Label         = obs.Name,
            Icon          = Icons.Material.Filled.Terrain,
            Description   = Describe("Observatory", Loc["SE_ObsSiteDescription", obs.Name].Value),
            Level         = SettingsNodeLevel.Group,
            Source        = SettingsNodeSource.Observatory,
            GroupKey      = "Observatory",
            ObservatoryId = obs.Id,
            DeviceNumber  = -1
        })];

    private SettingsNode Leaf(string label, string icon,
        SettingsNodeSource source, string groupKey) => new()
    {
        Label       = NodeLabel(label),
        Icon        = icon,
        Description = Describe(groupKey, label),
        Level       = SettingsNodeLevel.Group,
        Source      = source,
        GroupKey    = groupKey,
        DeviceNumber = -1
    };

    private SettingsNode DeviceLeaf(int deviceNumber, string label, string icon, string groupKey) => new()
    {
        Label        = NodeLabel(label),
        Icon         = icon,
        Description  = Describe(groupKey, label),
        Level        = SettingsNodeLevel.Group,
        Source       = SettingsNodeSource.Device,
        DeviceNumber = deviceNumber,
        GroupKey     = groupKey
    };

    // -- Node selection with unsaved-changes guard (Q4) ---------------------
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
            Loc["AddDevice_Title"],
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });

        var result = await dialog.Result;
        if (result.Canceled || result.Data is not ValueTuple<string, MountType, AlignmentMode> add)
            return;

        _deviceManagerBusy = true;
        try
        {
            var (deviceName, mountType, alignmentMode) = add;
            var nextDeviceNumber = _deviceWork.Keys.DefaultIfEmpty(-1).Max() + 1;

            await SettingsService.CreateDeviceForModeAsync(nextDeviceNumber, deviceName, mountType, alignmentMode);
            await SettingsService.AddAlpacaDeviceAsync(new AlpacaDevice
            {
                DeviceNumber = nextDeviceNumber,
                DeviceName = deviceName,
                DeviceType = "Telescope"
            });

            _hasPendingDeviceReload = true;
            await LoadSettingsAsync();
            SelectDeviceManagerNode();
            ShowSuccess(Loc["SE_DeviceAdded", nextDeviceNumber, deviceName]);
        }
        catch (Exception ex)
        {
            ShowError(Loc["SE_ErrAddDevice", ex.Message]);
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
            ShowError(Loc["SE_DeviceNotFound", deviceNumber]);
            return;
        }

        var parameters = new DialogParameters<DeleteDeviceDialog>
        {
            { d => d.DeviceNumber, deviceNumber },
            { d => d.DeviceName, device.DeviceName }
        };

        var dialog = await DialogService.ShowAsync<DeleteDeviceDialog>(
            Loc["SE_DeleteDeviceTitle"],
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

            _hasPendingDeviceReload = true;
            await LoadSettingsAsync();
            SelectDeviceManagerNode();
            ShowSuccess(Loc["SE_DeviceDeleted", deviceNumber, device.DeviceName]);
        }
        catch (Exception ex)
        {
            ShowError(Loc["SE_ErrDeleteDevice", deviceNumber, ex.Message]);
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
            .Select(kvp => Loc["SE_DeviceLabel", kvp.Key, kvp.Value.DeviceName].Value)
            .ToList();

        var parameters = new DialogParameters<HotReloadDevicesDialog>
        {
            { d => d.ConnectedDevices, connectedDevices }
        };

        var dialog = await DialogService.ShowAsync<HotReloadDevicesDialog>(
            Loc["HotReload_Title"],
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
                ShowSuccess(Loc["SE_HotReloaded", reloadResult.ReloadedCount]);
            }
            else
            {
                ShowError(Loc["SE_HotReloadFailed", reloadResult.ErrorMessage ?? string.Empty]);
            }
        }
        catch (Exception ex)
        {
            ShowError(Loc["SE_HotReloadError", ex.Message]);
        }
        finally
        {
            _deviceManagerBusy = false;
        }
    }

    // -- Dirty detection ----------------------------------------------------
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

        monitorSectionNode?.IsDirty = IsNodeDirty(monitorSectionNode);

        StateHasChanged();

        // Show success feedback
        ShowSuccess(Loc["SE_PresetApplied"]);
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

            leafNode?.IsDirty = IsNodeDirty(leafNode);
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
            "SelectAllDevices" => ["Device Filters"],
            "SelectAllCategories" => ["Category Filters"],
            "SelectAllTypes" => ["Message Type Filters"],
            "ClearAllFilters" => ["Device Filters", "Category Filters", "Message Type Filters"],
            _ => Array.Empty<string>()
        };

        switch (actionName)
        {
            case "SelectAllDevices":
                _monitorWork.ServerDevice = true;
                _monitorWork.Telescope = true;
                _monitorWork.Ui = true;
                ShowSuccess(Loc["SE_AllDevicesSelected"]);
                break;

            case "SelectAllCategories":
                _monitorWork.Other = true;
                _monitorWork.Driver = true;
                _monitorWork.Interface = true;
                _monitorWork.Server = true;
                _monitorWork.Mount = true;
                _monitorWork.Alignment = true;
                ShowSuccess(Loc["SE_AllCategoriesSelected"]);
                break;

            case "SelectAllTypes":
                _monitorWork.Information = true;
                _monitorWork.Data = true;
                _monitorWork.Warning = true;
                _monitorWork.Error = true;
                _monitorWork.Debug = true;
                ShowSuccess(Loc["SE_AllTypesSelected"]);
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
                ShowSuccess(Loc["SE_AllFiltersCleared"]);
                break;
        }

        // Mark affected leaf nodes and the section node as dirty
        MarkMonitorLeafNodesDirty(affectedGroupKeys);

        var monitorSectionNode = Flatten(_treeItems).FirstOrDefault(n => 
            n.Source == SettingsNodeSource.Monitor && 
            n.Level == SettingsNodeLevel.Section);

        monitorSectionNode?.IsDirty = IsNodeDirty(monitorSectionNode);

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

            ShowSuccess(Loc["SE_LoggingSaved"]);
        }
        catch (Exception ex)
        {
            ShowError(Loc["SE_ErrSaveLogging", ex.Message]);
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
        ShowSuccess(Loc["SE_LoggingReset"]);
    }

    // -- Save ---------------------------------------------------------------
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
            ShowSuccess(Loc["SE_Saved"]);
        }
        catch (Exception ex)
        {
            ShowError(Loc["SE_ErrSave", ex.Message]);
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
    private List<string> BuildBreakingChangeList(SkySettings original, SkySettings incoming)
    {
        var changes = new List<string>();

        if (!string.Equals(original.Mount, incoming.Mount, StringComparison.Ordinal))
            changes.Add(Loc["SE_Chg_MountType", original.Mount, incoming.Mount].Value);

        if (!string.Equals(original.Port, incoming.Port, StringComparison.OrdinalIgnoreCase))
            changes.Add(Loc["SE_Chg_ComPort", original.Port, incoming.Port].Value);

        if (original.BaudRate != incoming.BaudRate)
            changes.Add(Loc["SE_Chg_BaudRate", original.BaudRate, incoming.BaudRate].Value);

        if (!string.Equals(original.AlignmentMode, incoming.AlignmentMode, StringComparison.Ordinal))
            changes.Add(Loc["SE_Chg_AlignmentMode", original.AlignmentMode, incoming.AlignmentMode].Value);

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
            Loc["Breaking_Title"], parameters, options);
        var result  = await dialog.Result;

        return result is { Canceled: false };
    }

    // -- Reset --------------------------------------------------------------
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

    // -- Observatory location propagation dialog (Q7) -----------------------
    // -- Observatory CRUD --------------------------------------------------------------------------

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
            Loc["AddObservatory_Title"],
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
            { d => d.ContentText, Loc["SE_DeleteObsContent", obs.Name].Value },
            { d => d.ConfirmText, Loc["Common_Delete"].Value },
            { d => d.CancelText,  Loc["Common_Cancel"].Value }
        };
        var dialog = await DialogService.ShowAsync<ConfirmDialog>(
            Loc["SE_DeleteObsTitle"], parameters,
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
            { d => d.ContentText, Loc["SE_CopyObsContent", obs.Name, _deviceWork.Count].Value },
            { d => d.ConfirmText, Loc["SE_CopyAndSave"].Value },
            { d => d.CancelText,  Loc["Common_Cancel"].Value }
        };
        var dialog = await DialogService.ShowAsync<ConfirmDialog>(
            Loc["SE_CopyObsTitle"], parameters,
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

            ShowSuccess(Loc["SE_LocationCopied", obs.Name, _deviceWork.Count]);
        }
        catch (Exception ex)
        {
            ShowError(Loc["SE_ErrCopyObs", ex.Message]);
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

    // -- Unsaved-changes guard dialog (Q4) ----------------------------------
    private async Task<bool> ShowDiscardDialogAsync()
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.ContentText, Loc["SE_DiscardContent"].Value },
            { d => d.ConfirmText, Loc["SE_Discard"].Value },
            { d => d.CancelText,  Loc["SE_Stay"].Value }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall };
        var dialog  = await DialogService.ShowAsync<ConfirmDialog>(Loc["SE_UnsavedChangesTitle"], parameters, options);
        var result  = await dialog.Result;
        return result is { Canceled: false };
    }

    // -- Tree expand / collapse ---------------------------------------------
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

    // -- Search filter ------------------------------------------------------
    private bool MatchesSearch(SettingsNode node)
    {
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        return node.Label.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || node.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    // -- External settings-change callbacks --------------------------------
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

    // -- Helpers ------------------------------------------------------------
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
