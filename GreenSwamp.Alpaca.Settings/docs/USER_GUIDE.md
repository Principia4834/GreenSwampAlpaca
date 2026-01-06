# User Guide - Settings Profile Management

## Introduction

This guide explains how to use the Settings Profile Management system in GreenSwamp Alpaca Server to manage your telescope mount configurations.

## What are Profiles?

**Profiles** are complete sets of mount configuration settings. Each profile contains:
- Mount connection settings (port, baud rate)
- Location data (latitude, longitude, elevation)
- Mount configuration (home positions, park positions)
- Tracking settings (tracking rates, modes)
- All other mount-specific settings

## Why Use Profiles?

- **Multiple Mounts**: Switch between different telescope mounts
- **Different Locations**: Quick setup for different observatories
- **Testing**: Try different settings without losing your working configuration
- **Sharing**: Export and share configurations with other users
- **Backup**: Keep backup copies of your settings

## Alignment Modes

The system supports three alignment modes:

### German Equatorial (GermanPolar)
For German Equatorial Mounts (GEM) with counterweight on one side.
- **Home**: RA=0°, Dec=90° (pointing at celestial pole)
- **Park**: RA=180°, Dec=90° (counterweight down)

### Fork Equatorial (Polar)
For fork-mounted equatorial scopes.
- **Home**: RA=0°, Dec=0° (pointing at horizon)
- **Park**: RA=0°, Dec=90° (pointing at pole)

### Alt-Azimuth (AltAz)
For altitude-azimuth mounts.
- **Home**: Az=0°, Alt=0° (pointing north at horizon)
- **Park**: Az=0°, Alt=90° (pointing at zenith)

## Getting Started

### Accessing Profiles

1. Launch GreenSwamp Alpaca Server
2. Open web browser to `http://localhost:31426`
3. Click **Settings Profiles** in the navigation menu

### Understanding the Profile List

The profile list shows:
- **Name**: Profile identifier
- **Alignment Mode**: Mount type
- **Description**: Profile purpose
- **Created/Modified**: Timestamps
- **Status**: Active profile highlighted in **green**
- **Actions**: Buttons for profile operations

### Default Profiles

Three default profiles are automatically created:
- **German Equatorial (Default)** - For GEM mounts
- **Fork Equatorial (Default)** - For fork mounts
- **Alt-Azimuth (Default)** - For Alt-Az mounts

**Note**: Default profiles are **read-only** and cannot be edited or deleted.

## Creating a Profile

### Method 1: From Template

1. Click **"New Profile"** button
2. Enter a unique profile name
   - Use descriptive names: `my-eq6r`, `observatory-cgem`
   - Lowercase letters, numbers, hyphens recommended
3. Select **Alignment Mode** for your mount type
4. Leave **Copy From** empty
5. Click **"Create"**

The new profile is created with default settings for the selected mode.

### Method 2: Clone Existing Profile

1. Click **"New Profile"** button
2. Enter a new profile name
3. Select **Alignment Mode**
4. Select profile to **Copy From** dropdown
5. Click **"Create"**

The new profile is created with all settings from the source profile.

### Method 3: Clone from List

1. Find the profile you want to copy
2. Click **"Clone"** button
3. A new profile named `{original}-copy` is created
4. Rename if desired

## Editing a Profile

### Opening the Editor

1. Click **"Edit"** button on any profile
2. Profile editor opens with tabbed interface

**Note**: Default profiles open in read-only mode with a warning message.

### Editor Tabs

#### Profile Information
- **Display Name**: Friendly name shown in UI
- **Alignment Mode**: Mount type (affects available settings)
- **Description**: Notes about this profile

#### Connection Tab
- **Mount Type**: Simulator or SkyWatcher
- **Port**: Serial port (COM1, COM3, etc.)
- **Baud Rate**: Communication speed
- **Data Bits**: Serial data bits

#### Location Tab
- **Latitude**: Observer latitude (-90 to 90°)
  - North is positive, South is negative
  - Example: `51.5074` (London)
- **Longitude**: Observer longitude (-180 to 180°)
  - East is positive, West is negative
  - Example: `-0.1278` (London)
- **Elevation**: Height above sea level (meters)

#### Mount Config Tab
- **Home Axis X**: RA/Az home position (degrees)
- **Home Axis Y**: Dec/Alt home position (degrees)
- **Park Name**: Name of park position

#### Tracking Tab
- **Tracking Rate**: Sidereal, Lunar, Solar, King, Custom
- **Sidereal Rate**: Rate for star tracking (°/hour)
- **Auto Track**: Enable tracking on startup

### Saving Changes

1. Make desired changes in any tab
2. Click **"Save Changes"** button
3. Success message appears
4. Changes saved to disk

### Canceling Changes

Click **"Cancel"** button to return to profile list without saving.

## Activating a Profile

### Why Activate?

Activating a profile makes it the **active configuration** used by the mount.

### How to Activate

1. Find the profile you want to use
2. Click **"Activate"** button
3. Success message: "Profile '{name}' is now active"
4. **IMPORTANT**: **Restart the application** to apply changes

### Checking Active Profile

The active profile is:
- Highlighted in **green** in the profile list
- Shows **"Active"** badge in Status column
- Cannot have an **"Activate"** button (already active)

### Changing Active Profile

1. Activate a different profile
2. Old profile becomes inactive
3. New profile becomes active
4. **Restart required** to load new settings

## Profile Operations

### Refresh List

Click **"Refresh"** button to reload profiles from disk.

Use when:
- Profiles changed by external tool
- After importing profiles manually
- Troubleshooting display issues

### Clone Profile

**Purpose**: Create a copy to modify

**Steps**:
1. Click **"Clone"** on source profile
2. New profile created with `-copy` suffix
3. Edit the clone as needed

**Use Cases**:
- Modify a default profile (clone first)
- Try different settings
- Create backup before major changes

### Export Profile

**Purpose**: Save profile to file for backup or sharing

**Steps**:
1. Click **"Export"** on profile
2. Profile saved to Downloads folder
3. Filename: `{profile-name}-{YYYYMMDD}.json`

**Use Cases**:
- Backup before major changes
- Share configuration with others
- Move profile to another computer

### Delete Profile

**Purpose**: Remove unused profiles

**Steps**:
1. Click **"Delete"** on profile
2. Confirm deletion dialog
3. Profile permanently removed

**Restrictions**:
- Cannot delete default profiles
- Cannot delete active profile (activate different one first)

**Warning**: Deletion is permanent. Export first if you might need it later.

## Common Workflows

### Setting Up New Mount

1. Create new profile: Click **"New Profile"**
2. Name it after your mount: `my-celestron-cgem`
3. Select alignment mode: German Equatorial
4. Click **"Create"**
5. Click **"Edit"** on new profile
6. Set Connection settings (port, baud rate)
7. Set Location settings (lat, lon, elevation)
8. Configure Home/Park positions
9. Click **"Save Changes"**
10. Click **"Activate"**
11. **Restart application**

### Switching Between Mounts

1. Ensure each mount has its own profile
2. Click **"Activate"** on the mount you want to use
3. **Restart application**
4. Connect to mount

### Testing New Settings

1. **Clone** your working profile
2. **Edit** the clone
3. Make experimental changes
4. **Activate** the clone
5. **Restart** and test
6. If successful: Keep clone
7. If failed: **Activate** original and restart

### Changing Observing Location

1. **Clone** existing profile
2. Rename to new location: `mount-name-location`
3. **Edit** profile
4. Update Location tab (lat, lon, elevation)
5. **Save Changes**
6. **Activate** when at that location

## Tips and Best Practices

### Naming Conventions

**Good Names**:
- `my-cgem-home` - Mount and location
- `eq6r-observatory` - Mount type and site
- `portable-altaz` - Use case

**Poor Names**:
- `test1`, `new`, `backup` - Not descriptive
- Names with spaces or special characters

### Backup Strategy

1. **Export** your active profile weekly
2. Store exports in organized folder
3. Name with date: `config-20250120.json`
4. Keep old exports for rollback

### Before Major Changes

1. **Export** current profile
2. **Clone** profile
3. Test changes on clone
4. Keep original if issues occur

### Multiple Locations

Create profile for each location:
- `mount-home`
- `mount-remote-observatory`
- `mount-star-party`

### Documentation

Use **Description** field to note:
- Last modification reason
- Special settings explained
- Known issues or quirks

## Troubleshooting

### "Profile already exists" Error

**Cause**: Profile name already in use

**Fix**: Choose a different name

### Cannot Edit Profile

**Symptom**: Form fields disabled

**Cause**: Trying to edit default profile

**Fix**: **Clone** the profile first, then edit the clone

### Changes Not Applied

**Symptom**: Settings unchanged after editing

**Fix**:
1. Verify **"Save Changes"** was clicked
2. Check for success message
3. Ensure profile is **activated**
4. **Restart application** (required)

### Profile List Empty

**Symptom**: No profiles shown

**Fix**:
1. Click **"Refresh"**
2. Check browser console for errors (F12)
3. Verify application has write permissions
4. Check `%AppData%/GreenSwampAlpaca/` exists

### Active Profile Not Loading

**Symptom**: Settings don't match active profile

**Fix**:
1. Verify green highlight on correct profile
2. **Restart application** (settings load at startup)
3. Check `active-profile.txt` in AppData folder

## Advanced Features

### Manual Profile Editing

**Location**: `%AppData%/GreenSwampAlpaca/{version}/profiles/`

**Format**: JSON files

**Warning**: Edit only if you understand JSON format. Invalid JSON will prevent profile loading.

**After Manual Edit**:
1. Click **"Refresh"** in UI
2. Click **"Edit"** to verify changes
3. Reactivate profile if needed

### Sharing Profiles

1. **Export** profile
2. Send JSON file to recipient
3. Recipient places file in their profiles folder
4. Click **"Refresh"** to see it

**Alternative** (when UI import added):
1. Export profile
2. Share JSON file
3. Recipient uses Import feature

## Keyboard Shortcuts

_(Future feature)_

## FAQ

**Q: How many profiles can I create?**
A: Unlimited. Each profile is a small JSON file.

**Q: Do profiles backup my observing data?**
A: No. Profiles contain only configuration settings, not logs or observations.

**Q: Can I share profiles between computers?**
A: Yes. Export on one computer, import on another.

**Q: What happens if I delete the active profile?**
A: You can't. You must activate a different profile first.

**Q: Can I edit default profiles?**
A: No, but you can clone them and edit the copy.

**Q: Do profiles work with all mount types?**
A: Yes. Select appropriate alignment mode for your mount.

## Getting Help

- **Issues**: https://github.com/Principia4834/GreenSwampAlpaca/issues
- **Discussions**: GitHub Discussions
- **Documentation**: See other guides in `docs/` folder

## Next Steps

- Read [Developer Guide](DEVELOPER_GUIDE.md) to extend the system
- See [API Reference](API.md) for programmatic access
- Review [Architecture](ARCHITECTURE.md) to understand internals
