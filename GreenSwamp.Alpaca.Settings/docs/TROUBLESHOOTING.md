# Troubleshooting Guide - Settings Profiles

## Common Issues

### Profile Page Won't Load / UI Freezes

**Symptoms**:
- Clicking "Settings Profiles" does nothing
- Page loads but shows spinner forever
- UI becomes unresponsive

**Causes**:
- Service initialization blocking UI thread
- Async deadlock in Blazor Server

**Solutions**:
1. Check browser console (F12) for JavaScript errors
2. Check application logs for exceptions
3. Verify `SettingsProfileService` uses lazy async initialization
4. Restart application and try again

**Technical Fix**:
```csharp
// Ensure SettingsProfileService constructor doesn't block
public SettingsProfileService(...)
{
    // ? DON'T DO THIS:
    // InitializeDefaultProfiles(); // Blocking call
    
    // ? DO THIS:
    // Initialize on first use with EnsureInitializedAsync()
}
```

### Default Profiles Missing

**Symptoms**:
- No profiles shown on first run
- "Profile not found" errors

**Causes**:
- Permission denied to create files
- Initialization failure
- Incorrect AppData path

**Solutions**:
1. Check write permissions to `%AppData%/GreenSwampAlpaca/`
2. Run application as administrator (once)
3. Manually create directory: `%AppData%/GreenSwampAlpaca/{version}/profiles/`
4. Check logs for initialization errors

**Verification**:
```
Navigate to: %AppData%/GreenSwampAlpaca/
Should contain: {version}/profiles/ directory
Should have files: default-germanpolar.json, default-polar.json, default-altaz.json
```

### Profile Changes Not Applied

**Symptoms**:
- Edited profile but mount uses old settings
- Active profile shows green but settings unchanged

**Cause**:
- Settings loaded at application startup only
- Forgot to restart after activating profile

**Solution**:
**ALWAYS RESTART APPLICATION AFTER ACTIVATING A PROFILE**

**Why**:
Settings are loaded once at startup. Changing active profile updates the file, but doesn't reload settings into memory.

**Steps**:
1. Activate profile
2. Close application completely
3. Restart application
4. Settings now loaded from activated profile

### Cannot Edit Profile

**Symptoms**:
- All form fields are disabled/grayed out
- Warning message about read-only profile

**Cause**:
- Trying to edit a default profile

**Solution**:
1. Click **"Clone"** on the default profile
2. A new editable profile is created
3. Edit the cloned profile
4. Activate the clone if desired

**Why Default Profiles Are Read-Only**:
- Prevents accidental modification
- Ensures clean templates always available
- Users can safely experiment with clones

### "Profile Already Exists" Error

**Symptoms**:
- Cannot create profile with desired name
- Error message when clicking Create

**Cause**:
- Profile name already in use

**Solution**:
1. Choose a different name
2. OR: Delete existing profile first
3. OR: Use clone feature to create copy

**Naming Tips**:
- Add suffixes: `-v2`, `-test`, `-backup`
- Use descriptive names: `mount-location-purpose`
- Check existing profiles before creating

### Cannot Delete Profile

**Symptoms**:
- Delete button disabled or missing
- Error message when attempting deletion

**Causes**:
1. **Default Profile**: Cannot delete default profiles
2. **Active Profile**: Cannot delete currently active profile

**Solutions**:
- **For Default Profiles**: Cannot delete (by design)
- **For Active Profiles**:
  1. Activate a different profile
  2. Now you can delete the previously active one

### Profile List Empty After Startup

**Symptoms**:
- Profile page shows "No profiles found"
- Refresh doesn't help

**Causes**:
- Profile directory not created
- Initialization failed silently
- JSON files corrupted

**Solutions**:
1. Click **"Refresh"** button
2. Check logs for errors
3. Navigate to `%AppData%/GreenSwampAlpaca/{version}/profiles/`
4. Verify JSON files exist and are valid
5. If files corrupted, delete and restart app (recreates defaults)

### Validation Errors When Saving

**Symptom**: "Profile validation failed" message

**Common Validation Errors**:

#### Invalid Latitude
```
Error: Invalid latitude: 95.5
Fix: Must be between -90 and 90
     North is positive, South is negative
```

#### Invalid Longitude
```
Error: Invalid longitude: 185.0
Fix: Must be between -180 and 180
     East is positive, West is negative
```

#### Missing Mount Type
```
Error: Mount type is required
Fix: Set Mount field in Connection tab
```

#### Invalid Alignment Mode
```
Error: Invalid alignment mode
Fix: Select valid mode (GermanPolar, Polar, or AltAz)
```

### Export Fails

**Symptoms**:
- Export button doesn't work
- No file created in Downloads

**Causes**:
- Permission denied to Downloads folder
- Path doesn't exist
- Filename conflicts

**Solutions**:
1. Check Downloads folder permissions
2. Try exporting different profile
3. Manually export:
   ```
   Copy from: %AppData%/GreenSwampAlpaca/{version}/profiles/{name}.json
   To: Any location
   ```

### Template Load Errors

**Symptoms**:
- "Failed to load template" errors
- Cannot create new profiles

**Causes**:
- Embedded resources not included in build
- Resource names incorrect
- Template JSON invalid

**Solutions**:
1. Rebuild solution completely
2. Check `.csproj` has:
   ```xml
   <EmbeddedResource Include="Templates\*.json" />
   ```
3. Verify template files in `Templates/` folder
4. Validate JSON syntax in templates

### Active Profile Not Persisting

**Symptoms**:
- Set active profile but reverts after restart
- Wrong profile loads at startup

**Causes**:
- `active-profile.txt` not writable
- Profile name mismatch
- File deleted/corrupted

**Solutions**:
1. Check `%AppData%/GreenSwampAlpaca/{version}/active-profile.txt` exists
2. Verify it contains valid profile name
3. Set active profile again
4. Restart and verify

**Manual Fix**:
```
1. Navigate to: %AppData%/GreenSwampAlpaca/{version}/
2. Edit: active-profile.txt
3. Content should be: {profile-name}
4. Save and restart application
```

## Error Messages

### "Cannot delete default profile"

**Why**: Default profiles are protected to preserve templates

**Action**: Clone the default profile if you need to modify it

### "Cannot delete active profile"

**Why**: Active profile is currently in use

**Action**: Activate a different profile first, then delete

### "Profile '{name}' not found"

**Why**: Profile file doesn't exist or was deleted externally

**Action**: 
- Click Refresh to update list
- Create new profile with that name
- Check if file exists in profiles directory

### "Failed to deserialize profile"

**Why**: JSON file is corrupted or invalid

**Action**:
- Delete corrupted file
- Recreate profile
- OR: Manually fix JSON syntax

## Performance Issues

### Slow Profile Loading

**Symptoms**:
- Long delay when opening profile editor
- Slow response when clicking buttons

**Causes**:
- Large number of profiles
- Slow disk I/O
- Synchronous operations

**Solutions**:
- Reduce number of profiles (delete unused)
- Move AppData to faster drive
- Check disk health

### High Memory Usage

**Symptoms**:
- Application uses excessive RAM
- Slowdown over time

**Cause**:
- Profile objects not being garbage collected

**Solution**:
- Restart application periodically
- Report issue if persistent

## Browser-Specific Issues

### Chrome/Edge
- Clear cache if profiles not updating
- Check console (F12) for errors

### Firefox
- Disable strict privacy mode if issues
- Allow cookies for localhost

### Safari
- Check localStorage is enabled
- Allow WebSocket connections

## Network Issues

### Cannot Connect to UI

**Symptoms**:
- Browser can't reach localhost:31426
- "Site can't be reached" error

**Causes**:
- Firewall blocking port
- Application not started
- Port already in use

**Solutions**:
1. Verify application is running
2. Check firewall settings
3. Try different port in Program.cs
4. Check if another app using port 31426

## Data Recovery

### Recover Deleted Profile

**If you have export**:
- Manually copy JSON to profiles folder
- Click Refresh

**If no export**:
- Check Recycle Bin
- Use file recovery tool
- Recreate from scratch

### Recover Corrupted Profile

1. Export corrupted profile (if possible)
2. Edit JSON manually to fix syntax
3. Re-import or copy back to profiles folder
4. Click Refresh

### Backup Strategy

**Prevent Data Loss**:
1. Export profiles weekly
2. Store exports in version control
3. Keep multiple backups
4. Document profile purposes

## Getting Additional Help

### Check Logs

**Location**: Application output window or log files

**Look for**:
- Exceptions related to SettingsProfileService
- File I/O errors
- JSON deserialization errors

### Enable Debug Logging

```csharp
// In appsettings.json
{
  "Logging": {
    "LogLevel": {
      "GreenSwamp.Alpaca.Settings": "Debug"
    }
  }
}
```

### Report Issues

Include:
- Steps to reproduce
- Error messages
- Browser console output (F12)
- Application logs
- Operating system version
- .NET version

**GitHub Issues**: https://github.com/Principia4834/GreenSwampAlpaca/issues

## Advanced Troubleshooting

### Clear All Profiles (Reset)

**WARNING**: Deletes all custom profiles

```
1. Close application
2. Delete: %AppData%/GreenSwampAlpaca/{version}/profiles/
3. Restart application
4. Default profiles recreated
```

### Manual Profile Creation

```json
{
  "Name": "manual-profile",
  "DisplayName": "Manually Created",
  "Description": "Created manually",
  "AlignmentMode": "GermanPolar",
  "IsReadOnly": false,
  "Created": "2025-01-20T12:00:00Z",
  "LastModified": "2025-01-20T12:00:00Z",
  "Settings": {
    "Mount": "Simulator",
    "Port": "COM1",
    // ... more settings
  }
}
```

Save to: `%AppData%/GreenSwampAlpaca/{version}/profiles/manual-profile.json`

### Verify Service Registration

Check `Program.cs` has:
```csharp
builder.Services.AddVersionedSettings(builder.Configuration);
```

### Test Service Directly

```csharp
@inject ISettingsProfileService ProfileService

var profiles = await ProfileService.GetAllProfilesAsync();
// Should return at least 3 default profiles
```

## Still Having Issues?

1. Review [User Guide](USER_GUIDE.md)
2. Check [API Reference](API.md)
3. Read [Architecture](ARCHITECTURE.md)
4. Search existing [GitHub Issues](https://github.com/Principia4834/GreenSwampAlpaca/issues)
5. Create new issue with details
