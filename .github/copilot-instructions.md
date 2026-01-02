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

2. **UNDERSTAND THE FILE STRUCTURE**
   - This solution uses **partial classes extensively**
   - NEVER assume a method/field is missing without verification
   - Use `file_search` to locate all partial class files
   - Use `code_search` to find method/field definitions across files

3. **MAKE MINIMAL, TARGETED CHANGES**
   - Edit only what is necessary
   - Avoid large code block replacements
   - Use precise line ranges when possible

4. **VERIFY IMMEDIATELY AFTER CHANGES**
   ```
   run_build
   ```
   - Compare errors to baseline
   - **If NEW errors appear:** They are YOUR FAULT
   - Do NOT blame "pre-existing errors"
   - Revert or fix immediately

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

**Last Updated:** 2025-01-XX
**Solution:** GreenSwamp Alpaca Mount Control
**Maintainer:** Rob Morgan (robert.morgan.e@gmail.com)

---

## ?? Remember

> **Build first. Edit small. Verify immediately. Own your mistakes.**

This is a production astronomy mount control system. Breaking the build wastes telescope time and frustrates users. ALWAYS follow the verification workflow.
