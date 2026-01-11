# Monitor Settings Quick Reference

## 🎯 Essential Settings for Common Scenarios

### Default (Development)
```json
{
  "MonitorSettings": {
    "LogMonitor": true,
    "StartMonitor": true,
    "Information": true,
    "Warning": true,
    "Error": true
  }
}
```
✅ Captures profile loading  
✅ Captures mount operations  
✅ Reasonable file sizes  

---

### Production (Minimal)
```json
{
  "MonitorSettings": {
    "LogMonitor": false,
    "StartMonitor": true,
    "Data": false,
    "Debug": false
  }
}
```
✅ Session log only  
✅ Errors captured  
✅ Low disk usage  

---

### Troubleshooting
```json
{
  "MonitorSettings": {
    "LogMonitor": true,
    "Data": true,
    "Mount": true,
    "Driver": true
  }
}
```
✅ Detailed mount commands  
✅ Driver communications  
✅ Full diagnostics  

---

## 📊 What Gets Logged?

| Entry Type | Devices | Categories | Types | Result |
|------------|---------|------------|-------|--------|
| Profile loading | Server | Mount | Information | `LoadedFromProfile` |
| Mount commands | Telescope | Mount | Data | Command details |
| Errors | Any | Any | Error | Always logged |
| UI interactions | Server | Ui | Information | Button clicks, etc. |

**Formula:** `Logged = Device AND Category AND Type`

---

## 📁 Log File Locations

**Default:** `C:\Users\{You}\Documents\GSServer\`

| File | Always On? | Controlled By |
|------|-----------|---------------|
| `GSSessionLog*.txt` | ✅ Yes | `LogSession` |
| `GSMonitorLog*.txt` | ❌ No | `LogMonitor` + `StartMonitor` |
| `GSErrorLog*.txt` | ✅ Yes | Always |

---

## ⚠️ Critical Settings

| Setting | Value | Why |
|---------|-------|-----|
| `StartMonitor` | `true` | **REQUIRED** for any monitor logging |
| `LogSession` | `true` | Captures essential app lifecycle |
| `Error` | `true` | Essential for troubleshooting |

---

## 🚀 Quick Actions

### Enable Debug Logging
```json
{ "MonitorSettings": { "Debug": true, "Data": true } }
```

### Disable All Logging
```json
{ "MonitorSettings": { "LogMonitor": false, "LogSession": false } }
```
⚠️ **Not recommended** - errors won't be logged!

### Mount Operations Only
```json
{
  "MonitorSettings": {
    "Mount": true,
    "Driver": true,
    "Telescope": true,
    "ServerDevice": false
  }
}
```

---

## 📖 Full Documentation

See: `MONITOR_SETTINGS_GUIDE.md` for complete reference

---

**Last Updated:** 2026-01-11
