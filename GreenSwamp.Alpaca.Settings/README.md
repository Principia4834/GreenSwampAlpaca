# Settings Profile Management System

## Overview

The Settings Profile Management System provides a comprehensive solution for managing telescope mount configurations in the GreenSwamp Alpaca Server. It allows users to create, manage, and switch between different mount configuration profiles based on alignment modes (German Equatorial, Fork Equatorial, Alt-Azimuth).

## Features

- ? **Template-Based Profiles** - Pre-configured defaults for each alignment mode
- ? **Profile CRUD Operations** - Create, Read, Update, Delete profiles
- ? **Active Profile Management** - Switch between profiles at runtime
- ? **Import/Export** - Backup and share profiles
- ? **Validation** - Ensures profile integrity
- ? **Blazor UI** - Professional web interface for profile management
- ? **Thread-Safe** - Async operations with proper locking
- ? **Version-Aware** - Settings isolated by application version

## Quick Start

### For Users

1. Launch GreenSwamp Alpaca Server
2. Click **Settings Profiles** in navigation menu
3. View default profiles or create custom ones
4. Click **Activate** to switch profiles (requires restart)

### For Developers

```csharp
// Inject service
@inject ISettingsProfileService ProfileService

// Get all profiles
var profiles = await ProfileService.GetAllProfilesAsync();

// Create new profile
var profile = await ProfileService.CreateProfileAsync(
    "my-mount", 
    AlignmentMode.GermanPolar);

// Activate profile
await ProfileService.SetActiveProfileAsync("my-mount");
```

## Architecture

```
GreenSwamp.Alpaca.Settings/
??? Models/              - Data models
??? Services/            - Business logic
??? Templates/           - Embedded JSON templates
??? Extensions/          - DI registration
```

## File System

```
%AppData%/GreenSwampAlpaca/{version}/
??? profiles/
?   ??? default-germanpolar.json  (Read-only)
?   ??? default-polar.json        (Read-only)
?   ??? default-altaz.json        (Read-only)
?   ??? [user-profiles].json      (Editable)
??? templates/                     (Copied from embedded)
??? active-profile.txt             (Active profile name)
```

## API Reference

### ISettingsProfileService

| Method | Description |
|--------|-------------|
| `CreateProfileAsync` | Create new profile |
| `GetProfileAsync` | Get profile by name |
| `GetAllProfilesAsync` | Get all profiles |
| `UpdateProfileAsync` | Update profile |
| `DeleteProfileAsync` | Delete profile |
| `GetActiveProfileAsync` | Get active profile |
| `SetActiveProfileAsync` | Set active profile |
| `ValidateProfileAsync` | Validate profile |
| `ExportProfileAsync` | Export to JSON |
| `ImportProfileAsync` | Import from JSON |

See full API documentation in [API.md](docs/API.md)

## Configuration

### Register Services (Program.cs)

```csharp
builder.Services.AddVersionedSettings(builder.Configuration);
```

### Customize Templates

Edit files in `Templates/` directory and rebuild.

## Documentation

- **[User Guide](docs/USER_GUIDE.md)** - End-user documentation
- **[Developer Guide](docs/DEVELOPER_GUIDE.md)** - Development reference
- **[API Reference](docs/API.md)** - Detailed API documentation
- **[Architecture](docs/ARCHITECTURE.md)** - System design
- **[Troubleshooting](docs/TROUBLESHOOTING.md)** - Common issues

## Troubleshooting

### Profile Page Freezes

**Cause**: Blocking async calls in service initialization

**Fix**: Ensure `SettingsProfileService` uses lazy async initialization

### Default Profiles Missing

**Cause**: Permission issues or initialization failure

**Fix**: Check write permissions to `%AppData%/GreenSwampAlpaca/`

### Changes Not Applied

**Cause**: Profile loaded at startup only

**Fix**: **Restart application** after activating a profile

## Contributing

1. Fork repository
2. Create feature branch
3. Make changes
4. Submit pull request

## License

GNU General Public License v3.0

## Version

**v1.0.0** - Initial Release (2025)

---

**Documentation**: See `docs/` folder for detailed guides

**Support**: https://github.com/Principia4834/GreenSwampAlpaca/issues
