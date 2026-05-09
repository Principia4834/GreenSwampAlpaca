# Fix: Monitor / Logging Card - Save/Reset & Dirty Tracking

**Timestamp:** 2026-05-09 17:41  
**Commit:** `97507ad`  
**Branch:** master

---

## Issues Fixed

### 1. Missing Save and Reset Buttons
The Monitor / Logging settings card had preset and quick action buttons but lacked Save/Reset buttons needed to commit or discard changes.

### 2. Incorrect Dirty Tracking
All leaf nodes (Device Filters, Category Filters, Message Type Filters, Logging Options) were being marked as dirty when any preset or quick action was applied, even though only the section's working copy changed.

---

## Solutions Implemented

### 1. Added MudCardActions with Save/Reset Buttons

**File:** `MonitorLoggingSettingsCard.razor`

Added action buttons to the card:
```razor
<MudCardActions>
	<MudButton Variant="Variant.Filled" Color="Color.Primary"
			   StartIcon="@Icons.Material.Filled.Save"
			   OnClick="@(() => OnSave.InvokeAsync())">
		Save Changes
	</MudButton>
	<MudButton Variant="Variant.Outlined" Color="Color.Secondary"
			   StartIcon="@Icons.Material.Filled.RestartAlt"
			   OnClick="@(() => OnReset.InvokeAsync())">
		Reset
	</MudButton>
</MudCardActions>
```

Added two new EventCallbacks:
- `OnSave` — invoked when "Save Changes" is clicked
- `OnReset` — invoked when "Reset" is clicked

### 2. Fixed Dirty Tracking to Only Mark Section Node

**File:** `SettingsExplorer.razor.cs`

#### ApplyMonitorPresetAsync (Updated)
Changed from marking all Monitor nodes as dirty to only marking the section node:

```csharp
// OLD: Mark all Monitor group nodes as dirty
foreach (var node in Flatten(_treeItems).Where(n => n.Source == SettingsNodeSource.Monitor))
{
	node.IsDirty = IsNodeDirty(node);
}

// NEW: Mark only the Monitor section node as dirty
var monitorSectionNode = Flatten(_treeItems).FirstOrDefault(n => 
	n.Source == SettingsNodeSource.Monitor && 
	n.Level == SettingsNodeLevel.Section);

if (monitorSectionNode is not null)
{
	monitorSectionNode.IsDirty = IsNodeDirty(monitorSectionNode);
}
```

#### HandleMonitorQuickActionAsync (Updated)
Applied the same pattern — only the section node is marked dirty when quick actions are clicked.

### 3. Added Save/Reset Handlers

#### SaveMonitorSettingsAsync (New)
- Saves `_monitorWork` to settings service
- Updates `_monitorOrigJson` baseline
- Clears dirty flag on section node
- Shows success feedback

#### ResetMonitorSettingsAsync (New)
- Restores `_monitorWork` from `_monitorOrigJson`
- Clears dirty flag on section node
- Shows confirmation feedback

### 4. Wired Callbacks in SettingsExplorer.razor

Updated the card reference to include the new callbacks:
```razor
<MonitorLoggingSettingsCard OnPresetSelected="ApplyMonitorPresetAsync" 
						   OnQuickActionTriggered="HandleMonitorQuickActionAsync" 
						   OnSave="SaveMonitorSettingsAsync" 
						   OnReset="ResetMonitorSettingsAsync" />
```

---

## Behavior After Fix

### Presets
1. Click a preset button (Development, Production, Troubleshooting, Profile Debug)
2. `_monitorWork` is updated with preset values
3. **Only** the Monitor / Logging section node is marked dirty
4. Leaf nodes remain clean
5. "Save Changes" and "Reset" buttons appear enabled
6. Click "Save Changes" → settings saved, dirty flag cleared
7. Or click "Reset" → settings reverted, dirty flag cleared

### Quick Actions
1. Click a quick action button (All Devices, All Categories, All Types, Clear All)
2. `_monitorWork` filter values are updated
3. **Only** the Monitor / Logging section node is marked dirty
4. "Save Changes" and "Reset" buttons appear enabled
5. Same save/reset behavior as presets

### Visual Feedback
- Dirty badge only appears on the Monitor / Logging section node in the tree
- Action feedback messages confirm operations ("Preset applied", "All devices selected", etc.)
- Success/error messages appear for save operations

---

## Files Modified

| File | Changes |
|------|---------|
| `MonitorLoggingSettingsCard.razor` | Added MudCardActions, OnSave, OnReset callbacks |
| `SettingsExplorer.razor.cs` | Updated ApplyMonitorPresetAsync, HandleMonitorQuickActionAsync, added SaveMonitorSettingsAsync, added ResetMonitorSettingsAsync, fixed IsNodeDirty |
| `SettingsExplorer.razor` | Updated card invocation with new callbacks |

---

## Build Status

✅ **Build:** Success (0 errors, 125 warnings — pre-existing style warnings)

---

## Testing Checklist

- [ ] Open Settings Explorer → Click "Monitor / Logging" → Card appears with presets, quick actions, and Save/Reset buttons
- [ ] Click a preset button → Section node marked dirty, leaf nodes remain clean
- [ ] Click "Save Changes" → Settings saved, dirty flag cleared, badge disappears
- [ ] Click a quick action button → Section node marked dirty again
- [ ] Click "Reset" → Settings reverted, dirty flag cleared, badge disappears
- [ ] Make multiple changes → Verify all require save/reset handling
- [ ] Verify the section node is the only one showing dirty badge in the tree
- [ ] Confirm success/error messages appear appropriately

---

## Notes

- All monitor settings are edited at the section level (not at individual group/leaf level)
- The section node now accurately represents whether any monitor changes exist
- Leaf nodes remain visible in the tree but are not independently selectable (by design)
- Dirty tracking uses serialization comparison (Serialize(_monitorWork) vs _monitorOrigJson)
