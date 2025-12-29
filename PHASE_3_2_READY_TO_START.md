# Phase 3.2 - Ready to Start

## ? ALL PREPARATION COMPLETE

---

## Current Status

### Phase 3.1: COMPLETE ?
- New settings system integrated
- Services registered in DI
- Bridges active (93 properties synced)
- Build successful
- Zero breaking changes

### Phase 3.2: READY TO START ?
- Implementation plan created
- Files analyzed
- Strategy documented
- Risks identified
- Ready for Step 1

---

## What We Know

### Controllers Analysis ?

**14 Controller files found:**
- `ASCOM.Alpaca.Razor\Controllers\AlpacaController.cs`
- `ASCOM.Alpaca.Razor\Controllers\BaseController.cs` ? (No SkySettings usage)
- `ASCOM.Alpaca.Razor\Controllers\CameraController.cs`
- `ASCOM.Alpaca.Razor\Controllers\CoverCalibratorController.cs`
- `ASCOM.Alpaca.Razor\Controllers\DomeController.cs`
- `ASCOM.Alpaca.Razor\Controllers\FilterWheelController.cs`
- `ASCOM.Alpaca.Razor\Controllers\FocuserController.cs`
- `ASCOM.Alpaca.Razor\Controllers\LoginController.cs`
- `ASCOM.Alpaca.Razor\Controllers\ManagementController.cs`
- `ASCOM.Alpaca.Razor\Controllers\ObservingConditionsController.cs`
- `ASCOM.Alpaca.Razor\Controllers\RotatorController.cs`
- `ASCOM.Alpaca.Razor\Controllers\SafetyMonitorController.cs`
- `ASCOM.Alpaca.Razor\Controllers\SwitchController.cs`
- `ASCOM.Alpaca.Razor\Controllers\TelescopeController.cs`

**BaseController Status:**
- ? No `SkySettings` usage found
- ? No `Properties.SkyTelescope.Default` usage
- Already uses DI pattern
- All controllers inherit from it

---

## Files Currently Open

You have these files open:
1. `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`
2. `GreenSwamp.Alpaca.MountControl\SkySystem.cs`
3. `ASCOM.Alpaca.Razor\Controllers\BaseController.cs`
4. `GreenSwamp.Alpaca.Shared\Monitor.cs`
5. `GreenSwamp.Alpaca.MountControl\SkySettings.cs`
6. `GreenSwamp.Alpaca.Shared\Settings.cs`
7. `GreenSwamp.Alpaca.Settings\Models\SkySettings.cs`

**These map to:**
- **Step 1 (Controllers):** BaseController.cs ?
- **Step 3 (Services):** Telescope.cs, SkySystem.cs, Monitor.cs, Settings.cs
- **Step 4 (Static):** SkySettings.cs (old), SkySettings.cs (new)

---

## Step 1: Controllers - Ready to Execute

### What to Do:

1. **Search for controllers using SkySettings:**
   ```bash
   cd ASCOM.Alpaca.Razor/Controllers
   grep -l "SkySettings\." *.cs
   ```

2. **For each controller found:**
   - Add DI constructor
   - Inject `IVersionedSettingsService`
   - Replace static calls
   - Test

3. **Pattern:**
   ```csharp
   // Before
   public class SomeController : BaseController
   {
       public IActionResult Get()
       {
           var value = SkySettings.SomeProperty;
           return Ok(value);
       }
   }
   
   // After
   public class SomeController : BaseController
   {
       private readonly IVersionedSettingsService _settingsService;
       
       public SomeController(IVersionedSettingsService settingsService)
       {
           _settingsService = settingsService;
       }
       
       public IActionResult Get()
       {
           var settings = _settingsService.GetSettings();
           var value = settings.SomeProperty;
           return Ok(value);
       }
   }
   ```

4. **Test after each file:**
   ```bash
   dotnet build
   # Expected: Success
   
   dotnet run --project GreenSwamp.Alpaca.Server
   # Expected: Server starts, API works
   ```

---

## Expected Challenges

### Challenge 1: Property Name Differences
**Old:** `SkySettings.Port`
**New:** `settings.Port`

**Solution:** Names are the same ?

### Challenge 2: Static vs Instance
**Old:** `SkySettings.SomeProperty` (static)
**New:** `_settingsService.GetSettings().SomeProperty` (instance)

**Solution:** Get settings once, reuse:
```csharp
var settings = _settingsService.GetSettings();
var prop1 = settings.Property1;
var prop2 = settings.Property2;
```

### Challenge 3: Setting Values
**Old:** `SkySettings.Port = "COM4"; SkySettings.Save();`
**New:** Need to save entire object

**Solution:**
```csharp
var settings = _settingsService.GetSettings();
settings.Port = "COM4";
await _settingsService.SaveSettingsAsync(settings);
```

---

## Safety Checklist

Before starting Step 1:

- [x] Phase 3.1 complete
- [x] Build successful
- [x] Implementation plan reviewed
- [x] Files analyzed
- [ ] Git backup branch created
- [ ] Ready to make first edit

**Create backup:**
```bash
git checkout -b phase-3.2-backup
git push origin phase-3.2-backup
git checkout -b phase-3.2-step-1-controllers
```

---

## Success Criteria for Step 1

After completing Step 1, verify:

- [ ] All controllers checked for `SkySettings` usage
- [ ] Controllers using `SkySettings` updated to DI
- [ ] No static `SkySettings` calls in controller files
- [ ] Build successful (0 errors)
- [ ] Server starts without errors
- [ ] API endpoints return correct values
- [ ] Settings page still works
- [ ] Bridge still syncs changes

---

## What to Avoid

### ? DON'T:
1. Edit large files (SkyServer, SkySettings) in Step 1
2. Remove bridge code
3. Delete old SkySettings class
4. Make changes without testing
5. Skip git commits
6. Edit multiple steps at once

### ? DO:
1. Work file-by-file
2. Test after each change
3. Commit frequently
4. Keep bridge active
5. Follow the plan
6. Ask if uncertain

---

## Estimated Time for Step 1

| Task | Time | Total |
|------|------|-------|
| Search for usage | 30 min | 30 min |
| First controller | 1 hour | 1.5 hours |
| Remaining controllers | 1-2 hours | 2.5-3.5 hours |
| Testing | 1 hour | 3.5-4.5 hours |
| **Total** | | **~4 hours** |

**If no controllers use SkySettings:** 30 minutes to verify

---

## Next Commands to Run

```bash
# 1. Create backup branch
git checkout -b phase-3.2-backup
git push origin phase-3.2-backup

# 2. Create work branch
git checkout -b phase-3.2-step-1-controllers

# 3. Search for usage in controllers
cd ASCOM.Alpaca.Razor/Controllers
grep -n "SkySettings\." *.cs
grep -n "Properties\.SkyTelescope\.Default" *.cs

# 4. Count occurrences
grep -c "SkySettings\." *.cs | grep -v ":0$"

# 5. If found, start editing (using edit_file tool)
# 6. Test after each file
dotnet build

# 7. When Step 1 complete, commit
git add .
git commit -m "Phase 3.2 Step 1: Migrate controllers to new settings"
```

---

## Decision Required

**READY TO START STEP 1?**

**Option A:** ? **YES - Start Step 1** (RECOMMENDED)
- Low risk
- Controllers likely don't use SkySettings much
- Good learning exercise
- Can rollback easily

**Option B:** ? **NO - Need more analysis**
- What additional analysis needed?
- What concerns remain?

**Option C:** ?? **PAUSE - Review plan first**
- What needs clarification?
- What risks concern you?

---

## Approval to Proceed

Awaiting your decision:

**Type "START" to begin Step 1 (Controllers)**
**Type "ANALYZE" to search for usage first**
**Type "PAUSE" to review strategy**

---

**STATUS: ? READY**
**RISK: ?? LOW**  
**TIME: ~4 hours**
**STEP: 1 of 4**

**Recommendation: START with usage analysis, then proceed carefully file-by-file.**
