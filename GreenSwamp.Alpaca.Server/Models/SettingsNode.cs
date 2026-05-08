namespace GreenSwamp.Alpaca.Server.Models;

/// <summary>Which backing settings object owns this node.</summary>
public enum SettingsNodeSource { Observatory, ServerConfig, Monitor, Device }

/// <summary>Tree depth of a node.</summary>
public enum SettingsNodeLevel { Root, Section, Group }

/// <summary>
/// View-model for a single node in the Settings Explorer tree.
/// Branch nodes (Root / Section) are not editable — they expand/collapse only.
/// Leaf (Group) nodes represent an editable group of settings.
/// </summary>
public class SettingsNode
{
    /// <summary>Text shown in the tree item.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>MudBlazor icon string for this node.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Tooltip / hover text drawn from the Description field of the Settings Reference.
    /// Shown as a MudTooltip on the tree item.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Depth within the tree hierarchy.</summary>
    public SettingsNodeLevel Level { get; init; }

    /// <summary>Which settings backing object this node belongs to.</summary>
    public SettingsNodeSource Source { get; init; }

    /// <summary>
    /// Device number when Source == Device; -1 for all non-device nodes.
    /// </summary>
    public int DeviceNumber { get; init; } = -1;

    /// <summary>
    /// Matches the Group column in SETTINGS-REFERENCE.md.
    /// Used as a key to load the correct sub-component editor.
    /// </summary>
    public string GroupKey { get; init; } = string.Empty;

    /// <summary>Child nodes (sections contain groups; root contains sections).</summary>
    public List<SettingsNode> Children { get; init; } = [];

    /// <summary>True when the working copy for this group differs from the saved original.</summary>
    public bool IsDirty { get; set; }
}
