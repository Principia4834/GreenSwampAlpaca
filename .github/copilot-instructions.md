# GitHub Copilot Workspace Instructions
# GreenSwamp Alpaca Solution

## General instructions

- Always use my name "Andy" when referring to the "user"
- Always include and refresh a time and date stamp in any markdown document you create or update using the format "YYYY-MM-DD HH:MM"
- Always refresh the date and time stamp by querying the current system time - do not hardcode or reuse old timestamps
- After any PowerShell bulk write to a markdown file, always normalise line endings to CR/LF before finishing and set the correct BOM for UTF-8 encoding
- Never use the edit_file tool to make edits in files over 1500 lines, instead use surgical edits with get_file_with_lines to capture context and verify line numbers before editing
- When using the edit_file tool always confirm the context is unique and the line numbers are correct by first using get_file_with_lines to read the exact lines you plan to edit, and verifying the content matches what you expect to change
- After each phase of edits, always commit the changes with a one-line message such as "feat: add new feature X" or "fix: resolve issue Y". This includes committing changes after each edit phase.
- Prefer strictly evidence-based analysis and do not make speculative assertions without direct support from code/logs.
- The workspace is Windows/PowerShell-only. Flag any tool usage that looks Unix-like (e.g., grep-named search tools) even when it's an internal IDE tool rather than an actual shell command. Continue defaulting to PowerShell syntax for all real terminal commands, and briefly clarify when a non-terminal tool has a Unix-sounding name.
- Workspace commands and shell examples must assume Windows/PowerShell syntax; avoid Unix-style shell patterns/flags unless explicitly requested.

## SVG Icon Compositions
- Revise SVG icon compositions to avoid additive-looking overlap.
- Ensure clearly visible transparent spacing around overlay glyphs.
- Provide icon path data in native 24x24 coordinates without runtime scaling transforms.

## Shell & CLI guidance for Copilot suggestions

- Default shell: `powershell.exe`. Always generate PowerShell-compatible commands unless the user explicitly requests a different shell.
- When emitting shell examples or one-liners assume Windows PowerShell / PowerShell 7 compatibility. Prefer pipeline-style PowerShell idioms (e.g., `Get-ChildItem | Select-String`) over Unix-style flags.

Rules (must follow)
1. Prefer PowerShell syntax
   - Assume `powershell.exe` as the target shell for all generated terminal commands.
   - Use PowerShell cmdlets and parameter names (e.g., `-Path`, `-Pattern`, `-Recurse`, `-Include`, `-Exclude`, `-Filter`, `-CaseSensitive`).
   - Never use bash commands for actions on this Windows-based project unless the user explicitly requests a different shell (e.g., `bash`, `sh`, `zsh`).
2. Do NOT use Unix-style flags with PowerShell cmdlets
   - Never generate `-r` (or other short Unix flags) with PowerShell cmdlets (e.g., `Select-String -r` is invalid).
   - Replace recursive `-r` usage with PowerShell equivalents such as `-Recurse` or `Get-ChildItem -Recurse` where appropriate.
3. Provide a correct PowerShell alternative whenever Copilot would suggest a Unix-style command

## MudBlazor UI creation, editing and debugging

- When answering MudBlazor UI behavior questions in this workspace, consult the MudBlazor MCP docs before responding.

## Razor file creation, editing and debugging

When editing, creating or debugging a .razor file always refer to Microsoft Learn MCP, use:

- [Razor syntax reference for ASP.NET Core | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/mvc/views/razor?view=aspnetcore-10.0) 
- [Introduction to ASP.NET Web Programming Using the Razor Syntax (C#) | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/web-pages/overview/getting-started/introducing-razor-syntax-c) 
- [ASP.NET Core Razor components | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/?view=aspnetcore-10.0)

## ?? CRITICAL: ALWAYS FOLLOW THIS WORKFLOW

### Before Making ANY Changes:

1. **VERIFY BUILD STATE FIRST** run_build   - **If build fails:** STOP. Report the issue. Do NOT proceed with changes.
   - **If build succeeds:** Document this baseline state before proceeding.
   - **Record the baseline**: "Build SUCCESS - 0 errors"

2. **CAPTURE FILE STATE (MANDATORY)**# Before ANY edit, capture:
$linesBefore = (Get-Content "path/to/file.cs").Count
Write-Host "File has $linesBefore lines before edit"
3. **UNDERSTAND THE FILE STRUCTURE**
   - This solution uses **partial classes extensively**
   - NEVER assume a method/field is missing without verification
   - Use `file_search` to locate all partial class files
   - Use `code_search` to find method/field definitions across files

4. **MAKE MINIMAL, TARGETED CHANGES**
   - Edit only what is necessary
   - Avoid large code block replacements
   - Use precise line ranges when possible
   - **For files >1500 lines: Use get_file_with_lines for context, edit ONLY the specific section**

5. **VERIFY IMMEDIATELY AFTER EACH EDIT (MANDATORY)**# After EVERY edit_file call:

# Step 1: Check line count
$linesAfter = (Get-Content "path/to/file.cs").Count
$change = $linesAfter - $linesBefore
Write-Host "Line change: $change (expected: -1 for delete, +10 for add, etc.)"

# Step 2: If change is > ±10 from expected? STOP AND REVERT
if ([Math]::Abs($change - $expectedChange) > 10) {
    Write-Host "ERROR: Unexpected line count change! REVERTING..."
    # Ask user to revert
}

# Step 3: Check git diff
git diff --stat path/to/file.cs
# Should match expected change (e.g., "1 insertion(+), 1 deletion(-)")

# Step 4: Build
   run_build
6. **COMPARE BUILD RESULTS**
   - Baseline: X errors
   - After edit: Y errors
   - **If Y > X:** YOU broke it. Revert immediately.
   - **If Y < X:** Verify the fix actually worked.
   - **If Y = X:** Verify no new errors in different locations.


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

- **.NET 10.0** - Target framework
- **C# 12.0** - Language version
- **Blazor Server** - UI framework
- **MudBlazor** - UI components
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
# Step 1: Find ALL partial files for the class
file_search "SkyServer" 0

# Step 2: Search for the specific method
code_search "SkyTasks" "CalcCustomTrackingOffset"

# Step 3: Verify in context
get_file "path/to/found/file.cs"
### When Editing Partial Classes:

- ? **DO:** Make surgical edits to specific methods
- ? **DO:** Preserve all existing code structure
- ? **DO:** Verify other partial files aren't affected
- ? **DON'T:** Replace large code blocks
- ? **DON'T:** Assume methods don't exist elsewhere
- ? **DON'T:** Remove code without checking dependencies

---

## ?? Settings System (IMPORTANT)

2. **Use modern settings service:**// ? CORRECT
   IVersionedSettingsService settingsService
   var settings = settingsService.GetSettings();
3. **Settings file locations:**
   - Default settings: `appsettings.json`
   - User settings: `%AppData%/GreenSwampAlpaca/{version}/appsettings.user.json`

---

## ??? Common Operations Guide

### Adding a New Feature
1. run_build                           # Baseline
2. file_search "related_class" 0       # Find relevant files
3. code_search "related_method"        # Find existing implementations
4. get_file "path/to/file.cs"         # Review context
5. edit_file                           # Make minimal changes
6. run_build                           # Verify immediately
### Fixing a Bug

When I report a bug in code from the current session — read the affected file, find the cause, fix it. No architecture tracing unless the first fix fails

1. run_build                           # Confirm bug exists
2. code_search "error_method_name"     # Locate all occurrences
3. file_search "partial_class" 0       # Find all partial files
4. get_file "path/to/file.cs"         # Review full context
5. edit_file                           # Surgical fix
6. run_build                           # Verify fix
### Refactoring Code
1. run_build                           # CRITICAL: Establish baseline
2. get_files_in_project "project.csproj" # Map dependencies
3. code_search "method_to_refactor"    # Find all usages
4. Edit ONE file at a time
5. run_build after EACH edit           # Incremental verification
6. If build breaks: REVERT immediately
---

## ?? ANTI-PATTERNS (NEVER DO THIS)

### ? Assuming Pre-existing Errors

**WRONG:**
> "The build failed with errors about missing methods. These appear to be pre-existing issues with partial classes..."

**CORRECT:**
> "I broke the build. Let me revert and try a different approach."

### ? Large Block Replacements

**WRONG:**// Replace entire method body
private static bool MountConnect()
{
    // ...entire new implementation...
}
**CORRECT:**
// Target specific lines
try
{
    // ...existing code...
    
    // Changed: Use modern settings file
    var userSettingsPath = GetVersionedSettingsPath();
    
    // ...existing code...
}
### ? Skipping Build Verification

**WRONG:**edit_file ? edit_file ? edit_file ? run_build
**CORRECT:**
run_build ? edit_file ? run_build ? edit_file ? run_build
---

## ?? Commit Message Guidelines

When suggesting commits, use this format:
<type>: <short description>

<detailed description>

Changes:
- Specific change 1
- Specific change 2

Verification:
- Build status: ? Success
- Tests run: Yes/No
- Manual testing: Description
Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`

---

## ?? Debugging Workflow

### When Build Fails After Your Edit:

1. **Acknowledge responsibility:**
   > "My edit broke the build. Analyzing errors..."

2. **Compare to baseline:**
   - What errors are NEW?
   - What files are affected?

3. **Check partial files:**file_search "affected_class" 0
4. **Review your changes:**get_file_with_lines "edited_file.cs" [{"start": X, "end": Y}]
5. **Fix or revert:**
   - If quick fix: Apply and verify
   - If uncertain: Revert and restart with smaller scope

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
# 1. Acknowledge immediately
"I broke the build with my last edit. Reverting changes..."

# 2. Inform user of specific problem
"The edit to SkyServer.Core.cs removed code from a partial class..."

# 3. Suggest recovery action
"Please revert the commit or I can attempt a surgical fix by..."
---

## ?? Remember

> **Build first. Edit small. Verify immediately. Own your mistakes.**

This is a production astronomy mount control system. Breaking the build wastes telescope time and frustrates users. ALWAYS follow the verification workflow.
