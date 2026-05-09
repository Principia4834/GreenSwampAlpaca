# Cleanup: Monitor / Logging Card - Button Labels, Dirty Tracking & Leaf Node Marking

**Timestamp:** 2026-05-09 17:58  
**Commit:** `fb6ab16`  
**Branch:** master

---

## Summary of Changes

Completed three cleanup tasks for the Monitor / Logging settings card to match the existing Settings Explorer UI patterns:

1. ✅ Changed "Save Changes" button label to "Save"
2. ✅ Enabled Save/Reset buttons only when section is dirty
3. ✅ Marked appropriate leaf nodes as dirty based on what each action affects

---

## Detailed Changes

### 1. MonitorLoggingSettingsCard.razor

#### Button Label Update
```razor
<!-- Before -->
<MudButton ... OnClick="@(() => OnSave.InvokeAsync())">
	Save Changes
</MudButton>

<!-- After -->
<MudButton ... OnClick="@(() => OnSave.InvokeAsync())">
	Save
</MudButton>
```

#### Button Enablement with Parameters
Added two new `[Parameter]` properties:
- `IsDirty` — whether the Monitor / Logging section has unsaved changes
- `IsLoading` — whether a save operation is in progress

Updated button disabled states:
```razor
<MudButton Disabled="@(!IsDirty || IsLoading)" ... >Save</MudButton>
<MudButton Disabled="@(!IsDirty)" ... >Reset</MudButton>
```

Both buttons now:
- Only enable when section is dirty
- Disable when loading (Save button only)
- Follow the same pattern as other settings editors

### 2. SettingsExplorer.razor.cs

#### Added MarkMonitorLeafNodesDirty Helper Method
```csharp
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
```

#### Updated ApplyMonitorPresetAsync
Now marks **all affected leaf nodes** when a preset is applied:
- Device Filters
- Category Filters
- Message Type Filters
- Logging Options

```csharp
// Mark all affected leaf nodes (presets affect all categories)
var affectedGroupKeys = new[] { "Device Filters", "Category Filters", "Message Type Filters", "Logging Options" };
MarkMonitorLeafNodesDirty(affectedGroupKeys);
```

#### Updated HandleMonitorQuickActionAsync
Now marks **only the specific leaf nodes** affected by each action:

| Action | Affected Leaf Nodes |
|--------|-------------------|
| SelectAllDevices | Device Filters |
| SelectAllCategories | Category Filters |
| SelectAllTypes | Message Type Filters |
| ClearAllFilters | All three (Device, Category, Message Type) |

```csharp
// Determine which leaf nodes are affected by this action
var affectedGroupKeys = actionName switch
{
	"SelectAllDevices" => new[] { "Device Filters" },
	"SelectAllCategories" => new[] { "Category Filters" },
	"SelectAllTypes" => new[] { "Message Type Filters" },
	"ClearAllFilters" => new[] { "Device Filters", "Category Filters", "Message Type Filters" },
	_ => Array.Empty<string>()
};
```

### 3. SettingsExplorer.razor

Updated card invocation to pass the required parameters:

```razor
<MonitorLoggingSettingsCard IsDirty="@_selectedNode.IsDirty"
						   IsLoading="@_saving"
						   OnPresetSelected="ApplyMonitorPresetAsync" 
						   OnQuickActionTriggered="HandleMonitorQuickActionAsync" 
						   OnSave="SaveMonitorSettingsAsync" 
						   OnReset="ResetMonitorSettingsAsync" />
```

---

## Behavior After Changes

### Button Enablement
✅ Save button: Enabled only when section is dirty, shows spinner during save  
✅ Reset button: Enabled only when section is dirty  
✅ Both disabled when no changes have been made

### Preset Application
1. Click a preset button
2. ✅ **Section node** marked dirty
3. ✅ **All four leaf nodes** marked dirty (since presets affect all categories)
4. Save/Reset buttons enable
5. User can review changes or click Save/Reset

### Quick Actions
1. Click "All Devices" → Only "Device Filters" leaf marked dirty
2. Click "All Categories" → Only "Category Filters" leaf marked dirty
3. Click "All Types" → Only "Message Type Filters" leaf marked dirty
4. Click "Clear All" → All three leaf nodes marked dirty
5. **Section node** also marked dirty for overall tracking
6. Dirty badges appear only on affected leaves in the tree
7. Save/Reset buttons enable

### Visual Feedback
- Dirty badges appear on section and affected leaf nodes in the tree
- "Save" button disabled until changes are made
- "Reset" button disabled until changes are made
- Loading spinner appears on Save button during save operation
- Leaf nodes show unsaved indicator in tree view

---

## Files Modified

| File | Changes |
|------|---------|
| `MonitorLoggingSettingsCard.razor` | Added IsDirty/IsLoading parameters, updated button labels and disabled binding |
| `SettingsExplorer.razor.cs` | Added MarkMonitorLeafNodesDirty helper, updated ApplyMonitorPresetAsync to mark all leaves, updated HandleMonitorQuickActionAsync to mark specific leaves |
| `SettingsExplorer.razor` | Updated card invocation to pass IsDirty and IsLoading parameters |

---

## UI Pattern Consistency

The updated card now follows the same patterns as other settings editors in Settings Explorer:

| Feature | Pattern |
|---------|---------|
| Button Label | "Save" (not "Save Changes") |
| Button Disabling | `Disabled="@(!IsDirty \|\| _saving)"` for Save, `Disabled="@(!IsDirty)"` for Reset |
| Dirty Tracking | Mark section and affected leaf nodes |
| Progress Feedback | MudProgressCircular on Save button during operation |
| Node Dirty Badge | "Unsaved" chip on selected node header |

---

## Build Status

✅ **Build:** Success (0 errors, 125 pre-existing warnings)

---

## Testing Checklist

- [ ] Navigate to Settings Explorer → Monitor / Logging section
- [ ] Verify Save and Reset buttons are **disabled** initially
- [ ] Click "Development" preset
  - [ ] Section node shows dirty badge
  - [ ] All four leaf nodes show dirty badge
  - [ ] Save and Reset buttons **enabled**
- [ ] Click "All Devices" quick action
  - [ ] Reset to clear dirty state first
  - [ ] Only "Device Filters" leaf shows dirty badge
  - [ ] Section also shows dirty badge
  - [ ] Save and Reset buttons **enabled**
- [ ] Click "All Categories" quick action
  - [ ] Only "Category Filters" leaf shows dirty badge
  - [ ] Section shows dirty badge
- [ ] Click "All Types" quick action
  - [ ] Only "Message Type Filters" leaf shows dirty badge
  - [ ] Section shows dirty badge
- [ ] Click "Clear All" quick action
  - [ ] All three filter leaf nodes show dirty badge
  - [ ] Section shows dirty badge
- [ ] Click "Save" button
  - [ ] Spinner appears
  - [ ] Settings saved
  - [ ] Dirty badges disappear
  - [ ] Save and Reset buttons **disabled**
- [ ] Make changes and click "Reset"
  - [ ] Settings reverted
  - [ ] Dirty badges disappear
  - [ ] Save and Reset buttons **disabled**

---

## Notes

- Leaf node marking uses serialization comparison via `IsNodeDirty(leafNode)` — consistent with existing Settings Explorer pattern
- The section node is always marked dirty when any action is taken, ensuring overall dirty tracking works correctly
- The card now receives dirty state from parent, making it a proper child component that displays parent state
- Button disabled logic follows existing patterns: buttons only enable when there are actual changes to save
