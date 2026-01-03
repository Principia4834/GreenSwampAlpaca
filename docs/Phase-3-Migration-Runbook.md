# Phase 3 Migration Runbook

## ?? **Quick Reference Guide**

**Purpose**: Step-by-step procedures for executing Phase 3 migration safely  
**Audience**: Developers implementing Phase 3.1-3.5  
**Status**: Phase 3.0 Complete ?

---

## ?? **CRITICAL SAFETY RULES**

### **Before ANY Code Change**

```powershell
# 1. ALWAYS verify build first
run_build
# Expected: Build successful

# 2. Create feature branch
git checkout -b feature/phase3-{phase-number}-{description}

# 3. Commit frequently (after each successful step)
git add .
git commit -m "Phase 3.X: {description}"
```

### **Red Flags** ??

STOP immediately if you see:
- ? Build errors after your change
- ? File >2000 lines needs editing
- ? Partial class method already exists
- ? Breaking change to public API
- ? Hardware not responding in tests

---

## ?? **Phase Breakdown**

| Phase | Duration | Risk | Start Condition |
|-------|----------|------|-----------------|
| 3.0 | Week 1-2 | ?? LOW | Phase A-C complete |
| 3.1 | Week 3-4 | ?? MEDIUM | Phase 3.0 complete |
| 3.2 | Week 5-7 | ?? MEDIUM | Phase 3.1 verified |
| 3.3 | Week 8 | ?? LOW | Phase 3.2 complete |
| 3.4 | Week 9-10 | ?? MEDIUM | Phase 3.3 tested |
| 3.5 | Week 11-12 | ?? LOW | Phase 3.4 stable |

---

## ?? **PHASE 3.0: PREPARATION** ? COMPLETE

### **Deliverables** ?
- [x] 6 interface definitions created
- [x] Build verified (0 errors)
- [x] Architecture documented
- [x] This runbook created

### **Next**: Proceed to Phase 3.1

---

## ?? **PHASE 3.1: EXTRACT TO INSTANCE CLASS**

### **Objective**
Create `MountInstance` class that initially delegates to static `SkyServer`.

### **Prerequisites**
- ? Phase 3.0 complete
- ? Build successful
- ? Feature branch created

### **Step-by-Step Procedure**

#### **3.1.1: Create MountInstance Shell** (Day 1-2)

```powershell
# Create file
New-Item -Path "GreenSwamp.Alpaca.MountControl/MountInstance.cs" -ItemType File
```

**File Content Template**:
```csharp
namespace GreenSwamp.Alpaca.MountControl
{
    public class MountInstance : IMountController
    {
        private readonly string _id;
        private readonly SkySettingsInstance _settings;
        
        public MountInstance(string id, SkySettingsInstance settings)
        {
            _id = id ?? throw new ArgumentNullException(nameof(id));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }
        
        public string Id => _id;
        public bool IsConnected => SkyServer.IsMountRunning;
        public bool IsRunning => SkyServer.IsMountRunning;
        
        // Initially delegate to static
        public bool Connect() => SkyServer.Connect_Internal();
        public void Disconnect() => SkyServer.Disconnect_Internal();
        public void Start() => SkyServer.Start_Internal();
        public void Stop() => SkyServer.Stop_Internal();
        public void Reset() => SkyServer.Reset_Internal();
        public void EmergencyStop() => SkyServer.EmergencyStop_Internal();
        public Exception? GetLastError() => SkyServer.MountError;
    }
}
```

**Verify**:
```powershell
run_build
# Expected: Build successful
```

**Commit**:
```powershell
git add GreenSwamp.Alpaca.MountControl/MountInstance.cs
git commit -m "Phase 3.1.1: Create MountInstance shell with delegation"
```

---

#### **3.1.2: Rename Static Methods** (Day 3-4)

?? **CRITICAL**: This is the risky part. One method at a time!

**Procedure for EACH method**:

1. **Choose ONE method** (start with simple read-only)
2. **Read method in context**:
```powershell
# Example: Connect method around line 180
get_file "GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs" 170 200
```

3. **Rename method** (targeted edit):
```csharp
// BEFORE
private static bool MountConnect()
{
    // ... implementation ...
}

// AFTER (add _Internal suffix)
internal static bool MountConnect_Internal()  // Was private, now internal
{
    // ... same implementation ...
}
```

4. **Update callers** (if any):
```csharp
// Find all calls
code_search "MountConnect()"

// Update each caller
return MountConnect_Internal();  // Was: MountConnect()
```

5. **Verify build**:
```powershell
run_build
# Must succeed before continuing!
```

6. **Test**:
```powershell
# Run with simulator
# Verify mount connects
# Check logs for errors
```

7. **Commit**:
```powershell
git add .
git commit -m "Phase 3.1.2: Rename MountConnect ? MountConnect_Internal"
```

**Methods to Rename** (in order):

| Priority | Method | Risk | Notes |
|----------|--------|------|-------|
| 1 | `GetRawDegrees()` | ?? LOW | Read-only |
| 2 | `GetRawSteps()` | ?? LOW | Read-only |
| 3 | `ConvertStepsToDegrees()` | ?? LOW | Utility |
| 4 | `MountConnect()` | ?? MEDIUM | Core |
| 5 | `MountStart()` | ?? MEDIUM | Core |
| 6 | `MountStop()` | ?? MEDIUM | Core |
| ... | *(continue with others)* | | |

**Estimated**: 2-3 methods/day = 10-15 methods in Phase 3.1

---

#### **3.1.3: Update Static Facade** (Day 5)

**Edit `SkyServer.Core.cs`**:

```csharp
public static partial class SkyServer
{
    private static MountInstance? _defaultInstance;
    
    // Phase A: Instance field added
    private static SkySettingsInstance _settings;
    
    public static void Initialize(SkySettingsInstance settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        
        // Phase 3.1: Create default instance
        _defaultInstance = new MountInstance("default", settings);
        
        var monitorItem = new MonitorEntry
        {
            Datetime = HiResDateTime.UtcNow,
            Device = MonitorDevice.Server,
            Category = MonitorCategory.Server,
            Type = MonitorType.Information,
            Method = MethodBase.GetCurrentMethod()?.Name,
            Thread = Thread.CurrentThread.ManagedThreadId,
            Message = "SkyServer initialized with default instance"
        };
        MonitorLog.LogToMonitor(monitorItem);
    }
    
    // Public static API delegates to instance (when available)
    public static bool Connect()
    {
        return _defaultInstance?.Connect() ?? MountConnect_Internal();
    }
}
```

**Verify**:
```powershell
run_build
# Test with simulator
# Verify logs show "initialized with default instance"
```

**Commit**:
```powershell
git add .
git commit -m "Phase 3.1.3: Update static facade to use default instance"
```

---

### **Phase 3.1 Completion Checklist**

- [ ] `MountInstance.cs` created
- [ ] 10-15 static methods renamed `_Internal`
- [ ] Static facade updated to delegate
- [ ] Build successful
- [ ] Integration tests pass
- [ ] Hardware tested (simulator + real mount)
- [ ] Documentation updated

### **Rollback Procedure**

If something goes wrong:
```powershell
# 1. Identify last good commit
git log --oneline

# 2. Revert to last good state
git reset --hard <commit-hash>

# 3. Verify build
run_build

# 4. Analyze what went wrong
git diff HEAD <bad-commit>
```

---

## ?? **PHASE 3.2: MIGRATE CORE METHODS**

*(Full procedure will be added after Phase 3.1 complete)*

### **Preview: Migration Pattern**

For each method:
1. Choose method from priority list
2. Read current static implementation
3. Copy to `MountInstance` as instance method
4. Convert static fields to instance fields
5. Test thoroughly
6. Update static method to delegate
7. Commit

**Estimated**: 3-5 methods/week over 3 weeks

---

## ?? **PHASE 3.3: INSTANCE MANAGER**

*(Full procedure will be added after Phase 3.2 complete)*

### **Preview: Manager Creation**

1. Create `MountInstanceManager.cs`
2. Implement `IMountInstanceManager`
3. Register in DI (`Program.cs`)
4. Test multi-instance scenarios
5. Update documentation

---

## ?? **PHASE 3.4: UPDATE CONSUMERS**

*(Full procedure will be added after Phase 3.3 complete)*

### **Preview: Consumer Migration**

1. **Blazor Components**
   - Inject `IMountInstanceManager`
   - Use instance methods
   - Keep static fallback

2. **ASCOM Driver**
   - Optional DI constructor
   - Prefer instance, fallback to static
   - Test with ASCOM clients

---

## ?? **PHASE 3.5: CLEANUP**

*(Full procedure will be added after Phase 3.4 complete)*

### **Preview: Final Cleanup**

1. Remove unused `_Internal` methods
2. Simplify static facade
3. Optimize performance
4. Final documentation update

---

## ?? **Progress Tracking**

### **Weekly Checklist Template**

```markdown
### Week X: Phase 3.Y

**Date**: 2025-MM-DD

**Planned**:
- [ ] Task 1
- [ ] Task 2
- [ ] Task 3

**Completed**:
- [x] Task 1 ?
- [ ] Task 2 ?
- [ ] Task 3 ?

**Blockers**:
- None / [Description]

**Build Status**: ? / ?
**Test Status**: ? / ?

**Next Week**:
- Task A
- Task B
```

---

## ?? **Emergency Contacts**

### **When to Ask for Help**

- Build broken >30 minutes
- Hardware not responding
- Unclear how to proceed
- Test failures unexplained

### **Resources**

- **Architecture**: `docs/Phase-3.0-Architecture.md`
- **Guidelines**: `.github/copilot-instructions.md`
- **Phase Status**: `docs/Phase-A-C-Final-Report.md`

---

## ?? **Success Criteria**

### **Phase 3 Complete When**:

- ? All 6 interfaces implemented
- ? `MountInstance` fully functional
- ? Multiple instances supported
- ? Static facade maintains compatibility
- ? UI components use instance manager
- ? ASCOM driver updated
- ? All tests passing
- ? Documentation complete
- ? Performance acceptable
- ? Zero regressions

---

## ?? **Quick Command Reference**

```powershell
# Build
run_build

# Search
code_search "method_name"
file_search "filename"

# Get file
get_file "path/to/file.cs" start_line end_line

# Git
git status
git diff
git log --oneline -10
git commit -m "message"

# Rollback
git reset --hard HEAD~1  # Last commit
git revert <commit>      # Specific commit
```

---

**Runbook Version**: 1.0  
**Last Updated**: 2025-01-20  
**Status**: ? **READY FOR PHASE 3.1**
