# Phase B: Testing & Verification

## ?? **Overview**

**Objective**: Verify `SkySettingsInstance` wrapper completeness and integration without breaking existing functionality.

**Status**: Phase A Complete ??? Phase B In Progress  
**Date**: 2025-01-20

---

## ??? **Completed (Phase A)**

- ??? `SkySettingsInstance.cs` wrapper created (129 properties)
- ??? Documentation saved (`docs/SkySettingsInstance-Usage.md`)
- ??? Gap analysis completed (100% property coverage verified)
- ??? `SkySettingsBridge` exists with bidirectional sync
- ??? `SkyServer.Initialize()` method added

---

## ???? **Phase B Objectives**

### **1. Build Verification** (5 minutes)

**Goal**: Ensure solution compiles with no errors

```powershell
# Verify baseline build
run_build
```

**Expected**: BUILD SUCCESS - 0 errors

---

### **2. Smoke Test: Wrapper Initialization** (10 minutes)

**Goal**: Verify `SkySettingsInstance` can be initialized and accessed

#### Test Steps:

1. Check `Program.cs` for singleton initialization
2. Verify `SkyServer.Initialize()` is called
3. Test basic property access

#### Expected Code Pattern:

```csharp
// In Program.cs or startup
SkySettingsInstance.Initialize();
SkyServer.Initialize(SkySettingsInstance.Instance);

// Property access works
var settings = SkySettingsInstance.Instance;
var latitude = settings.Latitude; // Should not throw
```

---

### **3. Integration Test: Bridge Sync** (15 minutes)

**Goal**: Verify `SkySettingsBridge` synchronizes settings correctly

#### Test Scenarios:

| Scenario | Action | Expected Result |
|----------|--------|-----------------|
| **Static ??? Instance** | Change `SkySettings.Latitude = 51.5` | `SkySettingsInstance.Instance.Latitude == 51.5` |
| **Instance ??? Static** | Change `SkySettingsInstance.Instance.Longitude = -0.1` | `SkySettings.Longitude == -0.1` |
| **Side Effects** | Change `settings.MaxSlewRate = 3.0` | `SkyServer.SetSlewRates()` called |

#### Verification Code:

```csharp
// Test bidirectional sync
var oldValue = SkySettings.Latitude;
SkySettings.Latitude = 40.7128;

// Verify sync
var newValue = SkySettingsInstance.Instance.Latitude;
Assert.AreEqual(40.7128, newValue, "Bridge failed to sync");

// Test reverse
SkySettingsInstance.Instance.Longitude = -74.0060;
Assert.AreEqual(-74.0060, SkySettings.Longitude, "Reverse sync failed");
```

---

### **4. Regression Test: Existing Functionality** (20 minutes)

**Goal**: Ensure no breaking changes to existing code

#### Critical Areas to Test:

| Component | Test | Pass Criteria |
|-----------|------|---------------|
| **Mount Connection** | Start simulator | Connects without errors |
| **Settings UI** | Open settings page | Page loads, values display |
| **ASCOM Driver** | Connect via ASCOM | No exceptions thrown |
| **Park Position** | Read park axes | Correct values returned |
| **Tracking Rate** | Change tracking rate | UI updates correctly |

---

### **5. Property Coverage Verification** (10 minutes)

**Goal**: Confirm all 129 properties are accessible

#### Spot-Check Critical Properties:

```csharp
var settings = SkySettingsInstance.Instance;

// Connection (5 properties)
Assert.IsNotNull(settings.Mount);
Assert.IsNotNull(settings.Port);
Assert.IsTrue(settings.BaudRate > 0);
Assert.IsNotNull(settings.AlignmentMode);
Assert.IsFalse(settings.AtPark); // or whatever default

// Capabilities (read-only)
Assert.IsTrue(settings.CanFindHome != null);
Assert.IsTrue(settings.CanPark != null);
Assert.IsTrue(settings.CanPulseGuide != null);

// Complex types
Assert.IsNotNull(settings.ParkAxes);
Assert.AreEqual(2, settings.ParkAxes.Length);

// Side-effect properties
var oldRate = settings.MaxSlewRate;
settings.MaxSlewRate = 3.0;
// Verify SetSlewRates was called (check logs or internal state)
```

---

## ?? **Known Constraints** (from copilot-instructions.md)

### **Large File Warning**

- `SkyServer.Core.cs` is **~3000 lines** ??? **Use targeted edits only**
- **Never replace entire methods or switch statements**
- **Read specific line ranges** before editing

### **Partial Class Warning**

- `SkyServer` uses partial classes across multiple files
- **Always verify** methods don't exist in other partials
- Use `file_search` and `code_search` before adding methods

---

## ?????? **Phase B Execution Plan**

### **Step 1: Build Verification**

```powershell
# Step 1.1: Check current build state
run_build

# Step 1.2: If errors exist, categorize
# - Phase A related? Fix immediately
# - Pre-existing? Document and skip

# Expected: 0 errors
```

### **Step 2: Quick Property Access Test**

```powershell
# Step 2.1: Search for Initialize() calls
code_search "SkySettingsInstance.Initialize"

# Step 2.2: Verify usage patterns
code_search "SkySettingsInstance.Instance"

# Step 2.3: Check for any null reference exceptions in logs
```

### **Step 3: Bridge Functionality Test**

```powershell
# Step 3.1: Verify bridge initialization
code_search "SkySettingsBridge.Initialize"

# Step 3.2: Check for bidirectional event handlers
code_search "OnOldSettingsPropertyChanged"
code_search "OnNewSettingsChanged"

# Step 3.3: Verify sync methods exist
code_search "SyncNewToOld"
code_search "SyncOldToNew"
```

### **Step 4: Integration Smoke Test**

```powershell
# Step 4.1: Build and run application
run_build

# Step 4.2: Check startup logs for initialization messages
# Look for: "SkyServer initialized with instance settings"
#           "Enhanced bridge initialized with bidirectional event forwarding"

# Step 4.3: Verify no exceptions during startup
```

### **Step 5: Documentation Review**

```powershell
# Step 5.1: Verify documentation exists
ls docs/SkySettingsInstance-Usage.md

# Step 5.2: Check completeness
# - All 10 property categories documented?
# - Side effects warnings present?
# - Code examples included?
```

---

## ?? **Success Criteria**

| Criteria | Status | Notes |
|----------|--------|-------|
| ?? Build compiles with 0 errors | ??? | Baseline established |
| ?? `SkySettingsInstance` initializes | ? | To verify |
| ?? All 129 properties accessible | ? | Spot-check 10 |
| ?? Bridge syncs bidirectionally | ? | Test 2-3 properties |
| ?? No regressions in existing code | ? | Smoke test |
| ?? Documentation complete | ??? | Saved |
| ?? Side effects preserved | ? | Verify 3-5 properties |

---

## ???? **Risk Assessment**

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Build failures | Low | High | Run `run_build` first |
| Null reference exceptions | Medium | High | Check initialization order |
| Side effects lost | Low | Critical | Test hardware commands |
| Bridge desync | Low | Medium | Test property changes |
| Large file corruption | Low | Critical | **Use targeted edits only** |

---

## ?? **Next Steps After Phase B**

### **Phase C: Full Integration** (Future)

1. Migrate all UI components to use `SkySettingsInstance`
2. Update ASCOM driver to inject settings
3. Remove static `SkySettings` usage gradually
4. Add unit tests for all properties
5. Performance testing

### **Phase D: Cleanup** (Future)

1. Remove backward compatibility code
2. Simplify `SkySettingsBridge`
3. Final documentation update
4. Performance optimization

---

## ?? **Time Estimate**

| Task | Duration | Cumulative |
|------|----------|------------|
| Build verification | 5 min | 5 min |
| Property access test | 10 min | 15 min |
| Bridge sync test | 15 min | 30 min |
| Regression smoke test | 20 min | 50 min |
| Property coverage check | 10 min | 60 min |

**Total Phase B: ~60 minutes**

---

## ?? **Tools & Commands**

### **Verification Commands**

```powershell
# Build
run_build

# Search for usage patterns
code_search "SkySettingsInstance"
code_search "SkySettingsBridge"
code_search "SkyServer.Initialize"

# File checks
file_search "SkySettingsInstance.cs"
file_search "SkySettingsBridge.cs"

# Property verification
code_search "CanFindHome"
code_search "ParkAxes"
code_search "MaxSlewRate"
```

### **Critical Files to Review**

| File | Purpose | Line Count |
|------|---------|------------|
| `SkySettingsInstance.cs` | Wrapper | ~990 lines |
| `SkySettingsBridge.cs` | Bidirectional sync | ~400 lines |
| `SkyServer.Core.cs` | Mount control | ~3000 lines ??? **Caution!** |
| `Program.cs` | Startup | ~200 lines |
| `Telescope.cs` | ASCOM driver | ~2500 lines ??? **Caution!** |

---

## ??? **Phase B Completion Checklist**

- [ ] Build compiles with 0 errors
- [ ] `SkySettingsInstance.Initialize()` called in startup
- [ ] `SkyServer.Initialize()` called with instance
- [ ] Bridge initialized correctly
- [ ] 10 spot-check properties accessible
- [ ] 2-3 properties sync bidirectionally
- [ ] No startup exceptions
- [ ] Documentation reviewed and complete
- [ ] Side effects preserved (tested 3 properties)
- [ ] No regressions in existing functionality

---

**Phase B Owner**: AI Assistant  
**Review Status**: Pending  
**Approval Status**: Pending

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-20  
**Status**: ??? Phase B Ready to Execute
