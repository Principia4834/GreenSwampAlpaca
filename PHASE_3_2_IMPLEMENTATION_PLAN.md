# Phase 3.2: Code Migration - Implementation Plan

## Status: READY TO START

---

## Executive Summary

Phase 3.2 will gradually migrate code from the old static `SkySettings` class to the new DI-based `IVersionedSettingsService`. The migration will be done in **4 carefully sequenced steps** to minimize risk.

---

## Migration Strategy

### Core Principle: **Gradual, Testable, Reversible**

1. **Bridge remains active** - Both systems work simultaneously
2. **Test after each step** - Build and verify functionality
3. **No breaking changes** - Old code continues to work
4. **Can rollback easily** - Git commits per step

---

## Step-by-Step Plan

### **Step 1: Controllers** (Week 1) - EASY ?
**Risk: LOW** | **Effort: 2-3 days** | **Files: ~10**

#### Files to Migrate:
Based on your open files, likely candidates:
- `ASCOM.Alpaca.Razor\Controllers\BaseController.cs`
- Other telescope API controllers

#### Pattern:
```csharp
// OLD (static access)
public class TelescopeController : BaseController
{
    public IActionResult GetSomething()
    {
        var value = SkySettings.Port;
        return Ok(value);
    }
}

// NEW (DI injection)
public class TelescopeController : BaseController
{
    private readonly IVersionedSettingsService _settingsService;
    
    public TelescopeController(IVersionedSettingsService settingsService)
    {
        _settingsService = settingsService;
    }
    
    public IActionResult GetSomething()
    {
        var settings = _settingsService.GetSettings();
        var value = settings.Port;
        return Ok(value);
    }
}
```

#### Verification:
- Build successful
- API endpoints still work
- Bridge syncs changes
- Unit tests pass

---

### **Step 2: Blazor Pages** (Week 2) - MEDIUM ??
**Risk: MEDIUM** | **Effort: 3-4 days** | **Files: ~20**

#### Files to Migrate:
Based on your open files:
- `GreenSwamp.Alpaca.Server\Pages\MountSettings.razor` (already partially migrated?)
- Other settings pages

#### Pattern:
```razor
@page "/settings"
@inject IVersionedSettingsService SettingsService

<EditForm Model="_settings" OnValidSubmit="SaveSettings">
    <!-- form fields -->
</EditForm>

@code {
    private GreenSwamp.Alpaca.Settings.Models.SkySettings _settings = new();
    
    protected override void OnInitialized()
    {
        _settings = SettingsService.GetSettings();
        SettingsService.SettingsChanged += OnSettingsChanged;
    }
    
    private async Task SaveSettings()
    {
        await SettingsService.SaveSettingsAsync(_settings);
    }
    
    private void OnSettingsChanged(object? sender, GreenSwamp.Alpaca.Settings.Models.SkySettings newSettings)
    {
        _settings = newSettings;
        InvokeAsync(StateHasChanged);
    }
    
    public void Dispose()
    {
        SettingsService.SettingsChanged -= OnSettingsChanged;
    }
}
```

#### Verification:
- Build successful
- Settings page loads
- Can save settings
- Real-time updates work
- Bridge syncs changes

---

### **Step 3: Services** (Week 3) - MEDIUM ??
**Risk: MEDIUM** | **Effort: 4-5 days** | **Files: ~30**

#### Files to Migrate:
Based on your open files:
- `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`
- Other mount control services
- `GreenSwamp.Alpaca.Shared\Monitor.cs` (partially)
- `GreenSwamp.Alpaca.Shared\Settings.cs`

#### Pattern for Services:
```csharp
// OLD (static access)
public class Telescope
{
    public void Connect()
    {
        var port = SkySettings.Port;
        // ...
    }
}

// NEW (DI injection)
public class Telescope
{
    private readonly IVersionedSettingsService _settingsService;
    
    public Telescope(IVersionedSettingsService settingsService)
    {
        _settingsService = settingsService;
    }
    
    public void Connect()
    {
        var settings = _settingsService.GetSettings();
        var port = settings.Port;
        // ...
    }
}
```

#### Verification:
- Build successful
- Mount operations work
- Telescope driver functions
- Bridge syncs changes
- Integration tests pass

---

### **Step 4: Static Classes** (Week 4) - HARD ??????
**Risk: HIGH** | **Effort: 5-7 days** | **Files: 2-3 LARGE files**

#### Files to Migrate:
**?? EXTREME CARE NEEDED - THESE ARE MASSIVE FILES**

Based on your open files:
- `GreenSwamp.Alpaca.MountControl\SkySystem.cs` (unknown size)
- `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs` (~2500 lines)
- Related static utility classes

#### Strategy A: **Service Locator** (Quick, Low Risk)
```csharp
// Add to SkyServer.Core.cs
public static class SkyServer
{
    private static IVersionedSettingsService? _settingsService;
    
    // Called from Program.cs after DI configured
    public static void Initialize(IVersionedSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }
    
    // Update methods to use _settingsService
    public static void SomeMethod()
    {
        if (_settingsService == null)
            throw new InvalidOperationException("SkyServer not initialized");
            
        var settings = _settingsService.GetSettings();
        var port = settings.Port;
        // ...
    }
}

// In Program.cs (after app.Build()):
SkyServer.Initialize(app.Services.GetRequiredService<IVersionedSettingsService>());
```

**Pros:**
- ? Minimal code changes
- ? Low risk
- ? Quick to implement
- ? Easy to test

**Cons:**
- ?? Still static
- ?? Service locator anti-pattern
- ?? Technical debt

#### Strategy B: **Full DI Refactor** (Ideal, High Risk)
Convert `SkyServer` to instance class with proper DI.

**Pros:**
- ? Clean architecture
- ? Testable
- ? No technical debt

**Cons:**
- ???? Major refactoring
- ???? High risk of bugs
- ???? 2-3 weeks effort
- ???? Touches hundreds of call sites

#### **RECOMMENDATION: Strategy A (Service Locator)**
- Use for Phase 3.2
- Plan Strategy B for Phase 4 (future work)

#### Verification for Step 4:
- Build successful
- All mount operations work
- No static `SkySettings` calls remain
- Bridge can be disabled
- Full integration test suite passes

---

## File Analysis

### Large Files Requiring Special Handling

Based on search results and your open files:

1. **`GreenSwamp.Alpaca.MountControl\SkySettings.cs`** (~3500 lines)
   - **Action:** Mark for deprecation, not deletion
   - **Reason:** Bridge still needs it
   - **Timeline:** Remove in Phase 3.3+

2. **`GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`** (~2500 lines)
   - **Action:** Add service locator
   - **Strategy:** Careful incremental edits
   - **Timeline:** Step 4

3. **`GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`** (unknown size)
   - **Action:** Add DI constructor
   - **Strategy:** Inject `IVersionedSettingsService`
   - **Timeline:** Step 3

---

## Testing Strategy

### After Each Step:

1. **Build Test**
   ```bash
   dotnet build
   # Expected: Success with 0 errors
   ```

2. **Manual Test**
   ```bash
   dotnet run --project GreenSwamp.Alpaca.Server
   # Expected: Server starts, no errors
   ```

3. **Functional Test**
   - Navigate to settings page
   - Change a setting
   - Save
   - Restart server
   - Verify setting persisted

4. **Bridge Test**
   - Change setting via new system
   - Verify old system updates
   - Change setting via old system
   - Verify new system updates

---

## Risk Mitigation

### Critical Safety Measures:

1. **Git Commits Per Step**
   ```bash
   git checkout -b phase-3.2-step-1-controllers
   # Make changes
   git add .
   git commit -m "Phase 3.2 Step 1: Migrate controllers to new settings"
   # Test thoroughly
   # If good: merge
   # If bad: git reset --hard
   ```

2. **Backup Before Step 4**
   ```bash
   git checkout -b phase-3.2-backup-before-step-4
   git push origin phase-3.2-backup-before-step-4
   ```

3. **Bridge Stays Active**
   - Do NOT remove bridge until Phase 3.3
   - Both systems work simultaneously
   - Can rollback easily

4. **Feature Flags** (Optional)
   ```csharp
   public static class FeatureFlags
   {
       public static bool UseNewSettings { get; set; } = true;
   }
   
   // In code:
   var port = FeatureFlags.UseNewSettings 
       ? _settingsService.GetSettings().Port 
       : SkySettings.Port;
   ```

---

## Success Criteria Per Step

### Step 1: Controllers ?
- [ ] All controllers inject `IVersionedSettingsService`
- [ ] No static `SkySettings` calls in controllers
- [ ] Build successful
- [ ] API endpoints work
- [ ] Bridge syncs changes

### Step 2: Blazor Pages ?
- [ ] All pages inject `IVersionedSettingsService`
- [ ] No static `SkySettings` calls in pages
- [ ] Build successful
- [ ] Settings page works
- [ ] Real-time updates work
- [ ] Bridge syncs changes

### Step 3: Services ?
- [ ] All services inject `IVersionedSettingsService`
- [ ] No static `SkySettings` calls in services
- [ ] Build successful
- [ ] Mount operations work
- [ ] Telescope driver works
- [ ] Bridge syncs changes

### Step 4: Static Classes ?
- [ ] `SkyServer` has service locator
- [ ] All static classes updated
- [ ] No remaining static `SkySettings` calls (except deprecated class itself)
- [ ] Build successful
- [ ] All operations work
- [ ] Bridge can be disabled (optional)

---

## Timeline

| Step | Duration | Start | End | Status |
|------|----------|-------|-----|--------|
| Step 1: Controllers | 2-3 days | Day 1 | Day 3 | ? READY |
| Step 2: Blazor Pages | 3-4 days | Day 4 | Day 7 | ? PENDING |
| Step 3: Services | 4-5 days | Day 8 | Day 12 | ? PENDING |
| Step 4: Static Classes | 5-7 days | Day 13 | Day 19 | ? PENDING |
| **Total** | **14-19 days** | | | |

---

## Files to Search For Usage

Before starting each step, search for static usage:

```bash
# Find all SkySettings static access
git grep "SkySettings\." --exclude-dir=bin --exclude-dir=obj

# Find all Properties.SkyTelescope.Default access
git grep "Properties\.SkyTelescope\.Default" --exclude-dir=bin --exclude-dir=obj

# Count occurrences
git grep -c "SkySettings\." -- "*.cs" | sort -t: -k2 -nr | head -20
```

---

## Next Steps

### TO START PHASE 3.2:

1. **Review this plan** ?
2. **Create feature branch**
   ```bash
   git checkout -b phase-3.2-code-migration
   ```

3. **Start with Step 1** (Controllers)
   - Search for controller files
   - Identify static `SkySettings` usage
   - Add DI constructors
   - Test after each file

4. **Progress to Step 2, 3, 4** sequentially

---

## Decision Points

### MUST DECIDE NOW:

**Question 1:** Start with Step 1 (Controllers)?
- ? **YES** ? Low risk, good starting point
- ? **NO** ? Which step instead?

**Question 2:** Use Service Locator for Step 4?
- ? **YES** ? Quick, low risk (RECOMMENDED)
- ? **NO** ? Full DI refactor (high risk, 2-3 weeks)

**Question 3:** Keep bridge active during Phase 3.2?
- ? **YES** ? Safety net, can rollback (RECOMMENDED)
- ? **NO** ? Risky, no fallback

---

## Approval Required

Before proceeding, confirm:

- [ ] Plan reviewed and understood
- [ ] Git backup strategy approved
- [ ] Testing strategy approved
- [ ] Timeline acceptable
- [ ] Risk mitigation acceptable
- [ ] Ready to start Step 1

---

**STATUS: AWAITING APPROVAL TO START**

**Recommended First Action:** Start Step 1 (Controllers) - Low risk, good learning

**DO NOT proceed to Step 4 without completing Steps 1-3 successfully!**
