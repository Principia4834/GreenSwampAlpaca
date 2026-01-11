# Monitor Settings UI User Guide

## Accessing the Monitor Settings Page

**URL:** `http://localhost:5000/monitorsettings` (or your server address)

**Navigation:** Click "Monitor Settings" in the left sidebar menu

---

## Page Overview

The Monitor Settings UI provides a comprehensive interface for configuring all logging filters and options without editing JSON files.

---

## Quick Start Presets

At the top of the page, you'll find 4 preset configurations:

### 🔧 Development
**Use When:** Active development, debugging, or testing new features

**What It Does:**
- ✅ Enables ALL devices, categories, and types
- ✅ Maximum logging (including Data and Debug)
- ✅ Both Monitor and Session logs enabled
- ⚠️ Will generate large log files

**Best For:** Comprehensive debugging sessions

---

### ✅ Production
**Use When:** Running in production with users/observatory

**What It Does:**
- ✅ Server, Telescope, and Mount only
- ✅ Information, Warning, and Error types
- ❌ No Data or Debug logging
- ❌ GSMonitorLog disabled (Session log only)
- ✅ Low disk usage, essential errors captured

**Best For:** Day-to-day telescope operations

---

### 🐛 Troubleshooting
**Use When:** Debugging mount communication or driver issues

**What It Does:**
- ✅ Mount + Driver categories
- ✅ Data type enabled (command details)
- ✅ Both Monitor and Session logs
- ❌ Debug disabled (unless needed)

**Best For:** Mount not responding, slew issues, tracking problems

---

### 📄 Profile Debug
**Use When:** Diagnosing settings or profile loading issues

**What It Does:**
- ✅ Server + Mount categories only
- ✅ Information, Warning, Error types
- ✅ Focused on settings initialization
- ❌ Telescope operations filtered out

**Best For:** "Settings not loading", "Profile not found" issues

---

## Configuration Sections

### 1. Device Filters
**What They Control:** Which hardware/software components to log

| Device | When to Enable | Badge |
|--------|---------------|-------|
| **Server** | Always recommended (core operations) | Core |
| **Telescope** | For ASCOM API and mount operations | API |
| **UI** | Only when debugging button clicks, UI issues | High Vol ⚠️ |

---

### 2. Category Filters
**What They Control:** Which functional areas to log

| Category | What It Logs | Recommended? |
|----------|-------------|--------------|
| **Other** | Support/shared project code | Usually No |
| **Driver** | Mount driver communications | For mount issues |
| **Interface** | Interface-level operations | Sometimes |
| **Server** | Core server processes | Yes (Core) |
| **Mount** | Mount commands and state | Yes (Essential) |
| **Alignment** | Alignment model calculations | For alignment issues |

---

### 3. Type Filters (Log Levels)
**What They Control:** Severity/verbosity of messages

| Type | Volume | Writes To | Recommended? |
|------|--------|-----------|--------------|
| **Information** | Medium | Session + Monitor | ✅ Yes |
| **Data** | **Very High** | Monitor only | Only for debugging |
| **Warning** | Low | Session + Monitor | ✅ Yes |
| **Error** | Low | Error + Session + Monitor | ✅ Always |
| **Debug** | **Extremely High** | Monitor only | Only when desperate |

⚠️ **Performance Warning:** Enabling `Data` or `Debug` can generate **100-1000x** more log entries!

---

### 4. Logging Options

| Option | File | Purpose |
|--------|------|---------|
| **Start Monitor** | N/A | ⚠️ **REQUIRED** - Master switch for all logging |
| **Log Monitor** | `GSMonitorLog*.txt` | Detailed monitor entries |
| **Log Session** | `GSSessionLog*.txt` | Lifecycle + errors (always recommended) |
| **Log Charting** | `GSChartingLog*.txt` | Charting data (rarely needed) |

**Critical:** `Start Monitor` MUST be ON for any logging to work!

---

## "What Will Be Logged?" Preview

The preview panel shows:
- ✅ Which log files will be created
- 📊 How many filters are active
- 💡 The logging formula: `Device AND Category AND Type`

**Example:**
- 2 Devices enabled (Server, Telescope)
- 3 Categories enabled (Server, Mount, Driver)
- 3 Types enabled (Information, Warning, Error)
- **Result:** 18 possible combinations (2 × 3 × 3)

But only entries matching **ALL THREE** are logged!

---

## Quick Actions

| Button | What It Does |
|--------|--------------|
| **All Devices** | Select Server + Telescope + UI |
| **All Categories** | Select all 6 categories |
| **All Types** | Select all 5 types (incl. Data & Debug!) |
| **Clear All** | Uncheck everything (use with caution!) |

---

## Saving Changes

### Save Button
- **What It Does:**
  - Saves to `%AppData%\GreenSwampAlpaca\{version}\appsettings.user.json`
  - Immediately applies changes (no restart required)
  - Reloads `MonitorLog` filters

- **When to Use:**
  - After selecting a preset
  - After manual filter changes
  - After changing log path or language

---

### Reset to Defaults Button
- **What It Does:**
  - Resets to model defaults (see `MonitorSettings.cs`)
  - Does NOT save automatically
  - Shows "(not saved yet)" message

- **When to Use:**
  - Undo unwanted changes
  - Start fresh configuration
  - Return to known-good state

---

### Reload Button
- **What It Does:**
  - Reads settings from JSON file
  - Discards unsaved changes in UI
  - Useful if file was edited externally

- **When to Use:**
  - Another user changed settings
  - You edited JSON file manually
  - Suspect UI is out of sync

---

## Typical Workflows

### Scenario 1: First-Time Setup
1. Click **"Development"** preset
2. Click **"Save Changes"**
3. Start using application
4. Check logs in `C:\Users\{You}\Documents\GSServer\`

---

### Scenario 2: Mount Not Responding
1. Click **"Troubleshooting"** preset
2. Click **"Save Changes"**
3. Reproduce the issue
4. Check `GSMonitorLog` for mount commands
5. Look for errors or unusual responses
6. After fixed: Click **"Production"** preset and save

---

### Scenario 3: Profile Not Loading
1. Click **"Profile Debug"** preset
2. Click **"Save Changes"**
3. Restart application
4. Check `GSMonitorLog` for:
   - `LoadedFromProfile` (success)
   - `ProfileLoadFailed` (failure with reason)
5. After fixed: Restore previous settings

---

### Scenario 4: Too Many Logs
1. Click **"Production"** preset
2. Verify: `LogMonitor` is **OFF**
3. Verify: `Data` and `Debug` are **OFF**
4. Click **"Save Changes"**
5. Only `GSSessionLog` and `GSErrorLog` will be written

---

### Scenario 5: Minimize Disk Usage
1. **Clear All Filters** (button)
2. Select: **Server** device
3. Select: **Server** category
4. Select: **Information**, **Warning**, **Error** types
5. Uncheck: **Log Monitor** (only session log)
6. Click **"Save Changes"**

---

## Understanding the Formula

**An entry is logged if:**
```
Device enabled AND Category enabled AND Type enabled
```

**Example Entry:**
```
Device: Server
Category: Mount
Type: Information
Message: "Profile loaded successfully"
```

**For this to appear in logs:**
- ✅ `ServerDevice` must be checked
- ✅ `Mount` category must be checked
- ✅ `Information` type must be checked

**If ANY ONE is unchecked → Entry is NOT logged**

---

## Visual Indicators

### Badges
- 🔵 **Core** - Essential for operation
- 🟢 **Essential** - Highly recommended
- 🟡 **API** - For API/interface debugging
- 🟠 **High Vol** - Generates many entries
- 🔴 **Very High** - Generates LOTS of entries
- 🔴 **REQUIRED** - Must be enabled
- 🔵 **Session** - Also written to session log

### Alerts
- 🔴 **Red Alert** - StartMonitor is OFF (no logging!)
- 🟡 **Yellow Alert** - No filters enabled (nothing logged)

---

## Common Mistakes to Avoid

### ❌ Mistake 1: Disabling StartMonitor
**Problem:** `LogMonitor` is ON but `StartMonitor` is OFF  
**Result:** No `GSMonitorLog` file created  
**Fix:** Always keep `StartMonitor` = ON

---

### ❌ Mistake 2: Enabling Debug in Production
**Problem:** Left Debug type enabled after troubleshooting  
**Result:** 1GB+ log files per day  
**Fix:** Use presets or verify Debug is OFF before closing

---

### ❌ Mistake 3: Clearing All Filters
**Problem:** Clicked "Clear All" then saved  
**Result:** No logging at all (even errors!)  
**Fix:** Click "Reset to Defaults" then "Save"

---

### ❌ Mistake 4: Not Saving After Preset
**Problem:** Selected "Production" but didn't click "Save Changes"  
**Result:** Settings revert on next reload  
**Fix:** Always click "Save Changes" after selecting a preset

---

### ❌ Mistake 5: Ignoring the Preview
**Problem:** Enabled 100 filters, causing performance issues  
**Result:** Application slows down due to excessive logging  
**Fix:** Check preview panel - aim for <50 active combinations

---

## File Locations

### Settings File
```
%AppData%\GreenSwampAlpaca\{version}\appsettings.user.json
```

**Example:**
```
C:\Users\Andy\AppData\Roaming\GreenSwampAlpaca\8.2.1.31\appsettings.user.json
```

### Log Files
```
%USERPROFILE%\Documents\GSServer\
```

**Example:**
```
C:\Users\Andy\Documents\GSServer\
GSSessionLog2026-01-11-15.txt
GSMonitorLog2026-01-11-15.txt
GSErrorLog2026-01-11-15.txt
```

---

## Tips & Tricks

### 💡 Tip 1: Use Presets as Starting Points
Don't start from scratch - pick the closest preset and adjust

### 💡 Tip 2: Monitor the Preview Panel
It updates in real-time as you toggle checkboxes

### 💡 Tip 3: Session Log is Your Friend
`LogSession` is lightweight and captures essentials - always keep it ON

### 💡 Tip 4: Data Type for Commands
Enable `Data` type + `Mount` category to see every command sent to mount

### 💡 Tip 5: Quick Test Pattern
Development → Use → Troubleshooting → Production (as you refine)

---

## Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| Save | `Ctrl+Enter` (if focused in form) |
| Reload | `F5` (browser refresh) |
| Navigate to docs | Click "Documentation" button |

---

## Troubleshooting the UI

### Issue: Changes Not Saving
**Cause:** File permissions  
**Fix:** Check `%AppData%\GreenSwampAlpaca\` is writable

---

### Issue: UI Showing Old Values
**Cause:** File edited externally  
**Fix:** Click "Reload from File" button

---

### Issue: "Settings service not initialized"
**Cause:** Application startup failed  
**Fix:** Check console logs, restart application

---

## Summary

✅ **Use presets** for common scenarios  
✅ **Check the preview** before saving  
✅ **Keep StartMonitor ON** always  
✅ **Session log is essential**, Monitor log is optional  
✅ **Avoid Data/Debug** unless actively debugging  
✅ **Save after changes** - they apply immediately  

---

**Last Updated:** 2026-01-11  
**UI Version:** 2.0 (Enhanced)
