# GitHub Copilot Workspace Instructions
# GreenSwamp Alpaca Solution

## ?? CRITICAL: ALWAYS FOLLOW THIS WORKFLOW

### Before Making ANY Changes:

1. **VERIFY BUILD STATE FIRST**
   ```
   run_build
   ```
   - **If build fails:** STOP. Report the issue. Do NOT proceed with changes.
   - **If build succeeds:** Document this baseline state before proceeding.
   - **Record the baseline**: "Build SUCCESS - 0 errors"

2. **CAPTURE FILE STATE (MANDATORY)**
   ```powershell
   # Before ANY edit, capture:
   $linesBefore = (Get-Content "path/to/file.cs").Count
   Write-Host "File has $linesBefore lines before edit"
   ```

3. **UNDERSTAND THE FILE STRUCTURE**
   - This solution uses **partial classes extensively**
   - NEVER assume a method/field is missing without verification
   - Use `file_search` to locate all partial class files
   - Use `code_search` to find method/field definitions across files

4. **MAKE MINIMAL, TARGETED CHANGES**
   - Edit only what is necessary
   - Avoid large code block replacements
   - Use precise line ranges when possible
   - **For files >2000 lines: Use get_file_with_lines for context, edit ONLY the specific section**

5. **VERIFY IMMEDIATELY AFTER EACH EDIT (MANDATORY)**
   ```powershell
   # After EVERY edit_file call:
   
   # Step 1: Check line count
   $linesAfter = (Get-Content "path/to/file.cs").Count
   $change = $linesAfter - $linesBefore
   Write-Host "Line change: $change (expected: -1 for delete, +10 for add, etc.)"
   
   # Step 2: If change is > ±10 from expected ? STOP AND REVERT
   if ([Math]::Abs($change - $expectedChange) > 10) {
       Write-Host "ERROR: Unexpected line count change! REVERTING..."
       # Ask user to revert
   }
   
   # Step 3: Check git diff
   git diff --stat path/to/file.cs
   # Should match expected change (e.g., "1 insertion(+), 1 deletion(-)")
   
   # Step 4: Build
   run_build
   ```

6. **COMPARE BUILD RESULTS**
   - Baseline: X errors
   - After edit: Y errors
   - **If Y > X:** YOU broke it. Revert immediately.
   - **If Y < X:** Verify the fix actually worked.
   - **If Y = X:** Verify no new errors in different locations.

---

## ?? LARGE FILE HANDLING (3000+ lines)

### Critical Rules for Large Files:

**Files like `SkyServer.Core.cs` (3000+ lines) require special care:**

1. **NEVER replace entire switch statements or large blocks**
   - The edit_file tool can corrupt large structures
   - Edit ONLY the specific case/method/block you need to change

2. **Use targeted edits with context:**
   ```csharp
   // ? CORRECT - Minimal context
   case SomeCase:
       // ...existing code...
       newCode(); // Change here
       // ...existing code...
       break;
   ```

3. **For settings file copying in MountConnect():**
   - **Target ONLY the try-catch block** (lines ~318-335)
   - Do NOT include surrounding switch cases
   - Verify line numbers with get_file_with_lines first

4. **If edit fails:**
   - STOP immediately
   - Do NOT attempt multiple fixes
   - Ask user to revert
   - Try with smaller scope

### Example: Replacing Settings Code Block

**WRONG (too much context):**
```csharp
// Including entire switch case and surrounding code
```

**CORRECT (minimal context):**
```csharp
            try
            {
                // Get path to current version's appsettings.user.json file
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                // ...new implementation...
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                // ...error handling...
            }
```

---

## ?? Solution Architecture

### Project Structure

| Project | Purpose | Key Notes |
|---------|---------|-----------|
| **GreenSwamp.Alpaca.Server** | Blazor server application | Main entry point |
| **GreenSwamp.Alpaca.MountControl** | Mount control logic | **Heavy use of partial classes** |
| **GreenSwamp.Alpaca.Settings** | Modern .NET 8 settings (JSON) | Replaces legacy config |
| **GreenSwamp.Alpaca.Shared** | Shared utilities | Cross-project dependencies |
| **ASCOM.Alpaca.Razor** | ASCOM protocol implementation | External dependency |
| **GreenSwamp.Alpaca.Mount.SkyWatcher** | SkyWatcher mount driver | Hardware-specific |
| **GreenSwamp.Alpaca.Mount.Simulator** | Mount simulator | Testing/development |

### Technology Stack

- **.NET 8.0** - Target framework
- **C# 12.0** - Language version
- **Blazor Server** - UI framework
- **JSON configuration** - Modern settings (no XML user.config)

---

## ?? PARTIAL CLASSES: CRITICAL RULES

### Affected Classes

The following classes use partial class pattern:

1. **`SkyServer`** - Split across MULTIPLE files:
   - `SkyServer.Core.cs` - Core operations, initialization
   - `SkyServer.cs` - Main properties and state
   - `SkyServer.*.cs` - Other partial files (search before claiming missing)

2. **Other partial classes** - Always verify before editing

### Before Claiming "Method Not Found":

```bash
# Step 1: Find ALL partial files for the class
file_search "SkyServer" 0

# Step 2: Search for the specific method
code_search "SkyTasks" "CalcCustomTrackingOffset"

# Step 3: Verify in context
get_file "path/to/found/file.cs"
```

### When Editing Partial Classes:

- ? **DO:** Make surgical edits to specific methods
- ? **DO:** Preserve all existing code structure
- ? **DO:** Verify other partial files aren't affected
- ? **DON'T:** Replace large code blocks
- ? **DON'T:** Assume methods don't exist elsewhere
- ? **DON'T:** Remove code without checking dependencies

---

## ?? Settings System (IMPORTANT)

### Legacy vs Modern (Migration in Progress)

| Aspect | Legacy (.NET 4.8) | Modern (.NET 8) |
|--------|-------------------|-----------------|
| **Format** | XML (`user.config`) | JSON (`appsettings.user.json`) |
| **Location** | `%LocalAppData%` auto | `%AppData%/GreenSwampAlpaca/{version}/` |
| **Access** | `ConfigurationManager` | `IVersionedSettingsService` |
| **Status** | ? Being removed | ? Active |

### Rules for Settings Code:

1. **NO `System.Configuration` references**
   - Do not use `ConfigurationManager`
   - Do not reference .NET Framework config APIs

2. **Use modern settings service:**
   ```csharp
   // ? CORRECT
   IVersionedSettingsService settingsService
   var settings = settingsService.GetSettings();
   
   // ? WRONG
   ConfigurationManager.OpenExeConfiguration(...)
   Properties.Settings.Default.Port
   ```

3. **Settings file locations:**
   - Default settings: `appsettings.json`
   - User settings: `%AppData%/GreenSwampAlpaca/{version}/appsettings.user.json`

---

## ??? Common Operations Guide

### Adding a New Feature

```bash
1. run_build                           # Baseline
2. file_search "related_class" 0       # Find relevant files
3. code_search "related_method"        # Find existing implementations
4. get_file "path/to/file.cs"         # Review context
5. edit_file                           # Make minimal changes
6. run_build                           # Verify immediately
```

### Fixing a Bug

```bash
1. run_build                           # Confirm bug exists
2. code_search "error_method_name"     # Locate all occurrences
3. file_search "partial_class" 0       # Find all partial files
4. get_file "path/to/file.cs"         # Review full context
5. edit_file                           # Surgical fix
6. run_build                           # Verify fix
```

### Refactoring Code

```bash
1. run_build                           # CRITICAL: Establish baseline
2. get_files_in_project "project.csproj" # Map dependencies
3. code_search "method_to_refactor"    # Find all usages
4. Edit ONE file at a time
5. run_build after EACH edit           # Incremental verification
6. If build breaks: REVERT immediately
```

---

## ?? ANTI-PATTERNS (NEVER DO THIS)

### ? Assuming Pre-existing Errors

**WRONG:**
> "The build failed with errors about missing methods. These appear to be pre-existing issues with partial classes..."

**CORRECT:**
> "I broke the build. Let me revert and try a different approach."

### ? Large Block Replacements

**WRONG:**
```csharp
// Replace entire method body
private static bool MountConnect()
{
    // ...entire new implementation...
}
```

**CORRECT:**
```csharp
// Target specific lines
try
{
    // ...existing code...
    
    // Changed: Use modern settings file
    var userSettingsPath = GetVersionedSettingsPath();
    
    // ...existing code...
}
```

### ? Skipping Build Verification

**WRONG:**
```bash
edit_file ? edit_file ? edit_file ? run_build
```

**CORRECT:**
```bash
run_build ? edit_file ? run_build ? edit_file ? run_build
```

---

## ?? Commit Message Guidelines

When suggesting commits, use this format:

```
<type>: <short description>

<detailed description>

Changes:
- Specific change 1
- Specific change 2

Verification:
- Build status: ? Success
- Tests run: Yes/No
- Manual testing: Description
```

Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`

---

## ?? Debugging Workflow

### When Build Fails After Your Edit:

1. **Acknowledge responsibility:**
   > "My edit broke the build. Analyzing errors..."

2. **Compare to baseline:**
   - What errors are NEW?
   - What files are affected?

3. **Check partial files:**
   ```bash
   file_search "affected_class" 0
   ```

4. **Review your changes:**
   ```bash
   get_file_with_lines "edited_file.cs" [{"start": X, "end": Y}]
   ```

5. **Fix or revert:**
   - If quick fix: Apply and verify
   - If uncertain: Revert and restart with smaller scope

---

## ?? Key Files Reference

### Core Files (Edit with Extreme Care)

| File | Purpose | Partial Class? |
|------|---------|----------------|
| `SkyServer.Core.cs` | Mount core operations | ? Yes |
| `SkyServer.cs` | Main mount state/properties | ? Yes |
| `SkySystem.cs` | System initialization | ? No |
| `SkySettings.cs` | Static settings facade | ? No |
| `SkySettingsBridge.cs` | Settings DI bridge | ? No |

### Settings Files

| File | Purpose |
|------|---------|
| `GreenSwamp.Alpaca.Settings/Services/VersionedSettingsService.cs` | Settings service |
| `GreenSwamp.Alpaca.Settings/Models/SkySettings.cs` | Settings model |
| `GreenSwamp.Alpaca.Shared/Settings.cs` | Monitor settings |

### Entry Points

| File | Purpose |
|------|---------|
| `GreenSwamp.Alpaca.Server/Program.cs` | Application startup |
| `ASCOM.Alpaca.Razor/StartupHelpers.cs` | ASCOM configuration |

---

## ? Success Checklist

Before claiming a task is complete:

- [ ] Initial `run_build` succeeded (baseline documented)
- [ ] Changes are minimal and targeted
- [ ] Final `run_build` succeeds with NO new errors
- [ ] All partial class files were considered
- [ ] No assumptions made about "missing" code
- [ ] Changes follow existing code style
- [ ] Commit message is descriptive

---

## ?? When in Doubt

1. **Ask the user** - Don't guess
2. **Search first** - Use `code_search` and `file_search`
3. **Start small** - Make minimal changes
4. **Verify often** - Run `run_build` frequently
5. **Admit mistakes** - If you break it, own it immediately

---

## ?? Emergency Recovery

If you break the build:

```bash
# 1. Acknowledge immediately
"I broke the build with my last edit. Reverting changes..."

# 2. Inform user of specific problem
"The edit to SkyServer.Core.cs removed code from a partial class..."

# 3. Suggest recovery action
"Please revert the commit or I can attempt a surgical fix by..."
```

---

## ?? Remember

> **Build first. Edit small. Verify immediately. Own your mistakes.**

This is a production astronomy mount control system. Breaking the build wastes telescope time and frustrates users. ALWAYS follow the verification workflow.

---

## ?? Specific Task Instructions

### Task: Replace user.config with appsettings.user.json in MountConnect()

**Location:** `GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs` lines ~318-335

**Critical Requirements:**

1. **Use get_file_with_lines to read the exact block first:**
   ```
   get_file_with_lines "GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs" [{"start": 310, "end": 340}]
   ```

2. **Remove the using directive at the top:**
   - Line ~42: `using System.Configuration;` ? DELETE THIS

3. **Replace ONLY the try-catch block** (NOT surrounding code):
   ```csharp
   try
   {
       // Get path to current version's appsettings.user.json file
       var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
       var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
       
       // Get version from assembly (matches VersionedSettingsService logic)
       var infoVersionAttr = assembly
           .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
           .FirstOrDefault() as AssemblyInformationalVersionAttribute;
       
       var version = infoVersionAttr?.InformationalVersion 
           ?? assembly.GetName().Version?.ToString() 
           ?? "1.0.0";
       
       // Remove build metadata (e.g., +commitHash)
       var plusIndex = version.IndexOf('+');
       if (plusIndex > 0)
       {
           version = version.Substring(0, plusIndex);
       }
       
       var userSettingsPath = Path.Combine(appData, "GreenSwampAlpaca", version, "appsettings.user.json");
       var logDirectoryPath = GsFile.GetLogPath();
       
       if (File.Exists(userSettingsPath))
       {
           // Copy the appsettings.user.json file to the log directory
           var destinationPath = Path.Combine(logDirectoryPath, "appsettings.user.json");
           File.Copy(userSettingsPath, destinationPath, true);
           
           monitorItem = new MonitorEntry
           { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Copied appsettings.user.json to {logDirectoryPath}" };
           MonitorLog.LogToMonitor(monitorItem);
       }
       else
       {
           // Settings file doesn't exist yet - log info (it will be created later by the settings service)
           monitorItem = new MonitorEntry
           { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"appsettings.user.json not found at {userSettingsPath} - will be created on first settings save" };
           MonitorLog.LogToMonitor(monitorItem);
       }
   }
   catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
   {
       monitorItem = new MonitorEntry
       { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Cannot copy appsettings.user.json. {e.Message}" };
       MonitorLog.LogToMonitor(monitorItem);
   }
   ```

4. **Make TWO separate edits:**
   - Edit 1: Remove `using System.Configuration;` from top of file
   - Edit 2: Replace the try-catch block with new code

5. **Run build after EACH edit**

6. **DO NOT:**
   - Include surrounding case statements in the edit
   - Replace entire methods
   - Touch any switch statement structure
   - Edit more than the exact lines specified

**Rationale:** This replaces legacy .NET Framework XML config with modern JSON settings.
