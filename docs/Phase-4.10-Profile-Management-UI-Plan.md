# Phase 4.10: Profile Management UI - Implementation Plan

**Author:** AI Assistant  
**Date:** 2025-01-XX  
**Status:** 📋 Planning  
**Dependencies:** Phase 4.9 (Profile Loading Infrastructure - Complete)  
**Estimated Effort:** 11-13 hours (updated to include remote client API support)

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Background and Context](#background-and-context)
3. [Goals and Objectives](#goals-and-objectives)
4. [Current State Analysis](#current-state-analysis)
5. [Feature Requirements](#feature-requirements)
6. [UI/UX Design](#uiux-design)
7. [Implementation Tasks](#implementation-tasks)
8. [Testing Strategy](#testing-strategy)
9. [Success Criteria](#success-criteria)
10. [Risk Assessment](#risk-assessment)
11. [Timeline and Milestones](#timeline-and-milestones)
12. [Future Enhancements](#future-enhancements)

---

## Executive Summary

### What is Phase 4.10?

Phase 4.10 implements **user-facing profile management features** in the Blazor UI, enabling users to:
- View and browse available profiles
- Create new profiles from templates
- Edit existing profile settings
- Delete unused profiles
- Change device profile at runtime (dynamic devices only)
- Validate profile JSON before saving

### Why is This Important?

**Current Limitations (Phase 4.9):**
- Profiles must be manually created/edited in JSON files
- No UI for profile management
- Profile changes require server restart
- No validation before saving
- No easy way to create similar profiles (no templates)

**Phase 4.10 Benefits:**
- **User-Friendly:** Manage profiles through web UI
- **No Server Restart:** Change dynamic device profiles at runtime
- **Validation:** Prevent invalid configurations
- **Templates:** Quick profile creation from presets
- **Discovery:** Browse and preview available profiles

### Key Deliverables

1. **Profile Browser Page** - View, search, and filter profiles
2. **Profile Editor Component** - Create/edit profile settings
3. **Profile Templates** - Pre-configured starting points
4. **Runtime Profile Switching** - Change device profile (dynamic devices)
5. **Profile Validation** - JSON schema validation
6. **Profile Import/Export** - Backup and share profiles
7. **Remote Client API** - REST API endpoints for external applications to download/upload profiles with server-side validation

### Scope

**In Scope:**
- ✅ View profiles in UI
- ✅ Create profiles from templates
- ✅ Edit profile settings (form-based)
- ✅ Delete profiles
- ✅ Change profile for dynamic devices at runtime
- ✅ Validate profile settings
- ✅ Import/export profiles
- ✅ Remote client API for downloading/uploading profiles
- ✅ Server-side validation for remote uploads

**Out of Scope (Future Phases):**
- ❌ Change profile for reserved slots (requires server restart)
- ❌ Profile versioning/history
- ❌ Profile migration wizard
- ❌ Profile sharing marketplace
- ❌ Advanced profile scripting

---

## Background and Context

### Phase 4.9 Completion Status

**What Phase 4.9 Delivered:**
- ✅ Profile service infrastructure (`ISettingsProfileService`)
- ✅ Profile loading at startup (reserved slots)
- ✅ Profile loading via API (dynamic devices)
- ✅ 4 sample profiles (simulator-altaz, simulator-gem, simulator-polar, eq6-default)
- ✅ REST API endpoints (`GET /setup/profiles`, `POST /setup/devices`)
- ✅ Error handling and validation

**Phase 4.9 Known Limitation:**
- ⚠️ ASCOM API returns static values (deferred to Phase 5.x)
- **Impact on Phase 4.10:** None (UI-based features use instance directly)

### Architecture Foundation

**Existing Infrastructure:**

```
┌─────────────────────────────────────────────────────────────┐
│                    Profile Infrastructure                    │
│                     (Phase 4.9 - Complete)                   │
└─────────────────────────────────────────────────────────────┘
                              │
           ┌──────────────────┼──────────────────┐
           ▼                  ▼                  ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│ Profile Service │ │ Profile Files   │ │ REST API        │
│                 │ │                 │ │                 │
│ - LoadProfile() │ │ - JSON storage  │ │ - GET profiles  │
│ - GetProfiles() │ │ - File discovery│ │ - POST devices  │
│ - SaveProfile() │ │ - Validation    │ │ - Error handling│
└─────────────────┘ └─────────────────┘ └─────────────────┘
           │                  │                  │
           └──────────────────┼──────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Phase 4.10: UI Layer                      │
│                      (To Be Built)                           │
└─────────────────────────────────────────────────────────────┘
           │                  │                  │
           ▼                  ▼                  ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│ Profile Browser │ │ Profile Editor  │ │ Runtime Switch  │
│ Page            │ │ Component       │ │ Feature         │
└─────────────────┘ └─────────────────┘ └─────────────────┘
```

### User Scenarios

#### Scenario 1: Create Profile for New Mount

**User:** Amateur astronomer with EQ6-Pro mount

**Current Process (Phase 4.9):**
1. Manually edit JSON file in `profiles/` directory
2. Copy settings from similar profile
3. Manually enter serial port, baud rate, backlash
4. Restart server to test
5. Fix mistakes, repeat

**Desired Process (Phase 4.10):**
1. Navigate to Profile Browser page
2. Click "Create Profile from Template"
3. Select "SkyWatcher EQ6" template
4. Fill in form: serial port (COM3), backlash values
5. Click "Save and Test" → Preview in simulator
6. Apply to device without server restart

**Time Saved:** 10-15 minutes per profile creation

#### Scenario 2: Switch Between Simulator Modes

**User:** Developer testing different mount configurations

**Current Process (Phase 4.9):**
1. Edit `appsettings.json` to change ProfileName
2. Restart server
3. Test configuration
4. Repeat for each mode

**Desired Process (Phase 4.10):**
1. Navigate to Device Manager
2. Select device, click "Change Profile"
3. Choose profile from dropdown
4. Click "Apply" → Device reloads immediately
5. Test without restart

**Time Saved:** 30-60 seconds per switch

#### Scenario 3: Share Configuration with Community

**User:** Observatory operator with optimized settings

**Current Process (Phase 4.9):**
1. Manually copy JSON file
2. Email or post to forum
3. Recipient manually saves to profiles/ directory
4. Restart server

**Desired Process (Phase 4.10):**
1. Navigate to Profile Browser
2. Select profile, click "Export"
3. Download JSON file or copy link
4. Share link with community
5. Recipients click "Import from URL"
6. Profile automatically saved and available

**Benefit:** Easy community sharing and collaboration

---

## Goals and Objectives

### Primary Goals

1. **User-Friendly Profile Management**
   - Intuitive UI for non-technical users
   - No manual JSON editing required
   - Form-based editing with validation
   - Clear error messages

2. **Runtime Profile Switching**
   - Change device profile without server restart (dynamic devices)
   - Immediate application of new settings
   - Validation before applying
   - Rollback on failure

3. **Profile Discovery and Organization**
   - Browse available profiles
   - Search and filter by mount type, alignment mode
   - Preview profile settings before use
   - Group profiles by category (simulator, physical, custom)

4. **Profile Creation and Editing**
   - Create from templates (quick start)
   - Create from existing profile (copy/modify)
   - Create from scratch (advanced users)
   - Edit existing profiles with validation

5. **Data Safety**
   - Validate settings before save
   - Prevent invalid configurations
   - Backup profiles before editing
   - Restore previous version on failure

### Secondary Goals

1. **Profile Import/Export**
   - Export profiles for backup
   - Import profiles from file
   - Share profiles with others

2. **Profile Templates**
   - Pre-configured starting points
   - Common mount types (EQ6, AZ-GTi, etc.)
   - Different simulator modes
   - Easy customization

3. **Profile Validation**
   - JSON schema validation
   - Range checking (e.g., latitude -90 to 90)
   - Required field validation
   - Custom validation rules

### Non-Goals (Future Phases)

- ❌ **Change Reserved Slot Profiles at Runtime** - Requires server restart (architectural limitation)
- ❌ **Profile Versioning** - Track changes over time (Phase 4.11+)
- ❌ **Profile Migration Wizard** - Convert old formats (future)
- ❌ **Multi-User Profile Sharing** - Central repository (future)
- ❌ **Advanced Profile Scripting** - Computed values, macros (future)

---

## Current State Analysis

### Existing UI Pages

**Pages Available:**
- `DeviceManager.razor` - Device list and status
- `TelescopeSetup.razor` - Device setup form
- Device-specific setup pages (Telescope/0, Telescope/1, etc.)

**Current Profile Display:**
```razor
<!-- DeviceManager.razor (approximate location) -->
<div class="device-info">
    <span class="profile-name">Profile: @device.ProfileName</span>
    <span class="alignment-mode">Alignment: @device.AlignmentMode</span>
</div>
```

**Limitations:**
- Read-only display
- No way to change profile
- No way to view profile details
- No profile management features

### Existing REST API Endpoints

**Available Endpoints:**

| Endpoint | Method | Purpose | Phase |
|----------|--------|---------|-------|
| `/setup/profiles` | GET | List available profiles | 4.9 ✅ |
| `/setup/devices` | GET | List devices | 4.8 ✅ |
| `/setup/devices` | POST | Add device with profile | 4.9 ✅ |
| `/setup/devices/{id}` | DELETE | Remove device | 4.8 ✅ |

**Missing Endpoints (Phase 4.10):**

| Endpoint | Method | Purpose | Priority |
|----------|--------|---------|----------|
| `/setup/profiles/{name}` | GET | Get profile details | 🔴 Critical |
| `/setup/profiles` | POST | Create new profile | 🔴 Critical |
| `/setup/profiles/{name}` | PUT | Update profile | 🔴 Critical |
| `/setup/profiles/{name}` | DELETE | Delete profile | 🟡 High |
| `/setup/profiles/{name}/validate` | POST | Validate profile JSON | 🟡 High |
| `/setup/devices/{id}/profile` | PUT | Change device profile | 🔴 Critical |
| `/setup/profiles/templates` | GET | List profile templates | 🟢 Medium |
| `/setup/profiles/import` | POST | Import profile from file | 🟢 Medium |
| `/setup/profiles/{name}/export` | GET | Export profile | 🟢 Medium |
| **`/api/profiles`** | **GET** | **List profiles with metadata (remote client)** | **🔴 Critical** |
| **`/api/profiles/{name}`** | **GET** | **Download single profile (remote client)** | **🔴 Critical** |
| **`/api/profiles/{name}`** | **PUT** | **Upload/update profile (remote client)** | **🔴 Critical** |
| **`/api/profiles/{name}`** | **HEAD** | **Check profile existence (remote client)** | **🟡 High** |
| **`/api/profiles/{name}/metadata`** | **GET** | **Get metadata only (remote client)** | **🟡 High** |
| **`/api/profiles/download/all`** | **GET** | **Download all profiles bulk (remote client)** | **🟢 Medium** |
| **`/api/profiles/upload/bulk`** | **POST** | **Upload multiple profiles (remote client)** | **🟢 Medium** |
| **`/api/profiles/{name}/validate`** | **POST** | **Validate without saving (remote client)** | **🟡 High** |

### Existing Profile Service

**Interface:** `ISettingsProfileService`

```csharp
public interface ISettingsProfileService
{
    Task LoadProfileByNameAsync(string profileName, SkySettingsInstance instance);
    Task<IEnumerable<string>> GetProfileNamesAsync();
    bool ProfileExists(string profileName);
    // Missing methods for Phase 4.10:
    // - Task<ProfileModel> GetProfileDetailsAsync(string profileName);
    // - Task SaveProfileAsync(string profileName, ProfileModel profile);
    // - Task DeleteProfileAsync(string profileName);
    // - Task<bool> ValidateProfileAsync(ProfileModel profile);
}
```

**Implementation Gaps:**
- No method to get full profile details
- No method to save/update profiles
- No method to delete profiles
- No validation method
- No template support

---

## Feature Requirements

### Feature 1: Profile Browser Page

**Description:** Main page for viewing and managing profiles

**User Story:**
> As a user, I want to see all available profiles in one place, so I can choose the right configuration for my mount.

**Requirements:**

**FR1.1: Profile List Display**
- Display all profiles in a grid or list view
- Show profile name, mount type, alignment mode
- Show last modified date
- Show "In Use" indicator for profiles currently loaded
- Support pagination (if >20 profiles)

**FR1.2: Profile Search and Filter**
- Search by profile name
- Filter by mount type (Simulator, SkyWatcher, etc.)
- Filter by alignment mode (AltAz, Polar, GermanPolar)
- Filter by status (In Use, Available)
- Clear filters button

**FR1.3: Profile Actions**
- View profile details (read-only)
- Edit profile (opens editor)
- Delete profile (with confirmation)
- Duplicate profile (copy with new name)
- Export profile (download JSON)
- Create new profile (opens editor with template selection)

**FR1.4: Profile Preview**
- Click profile card to see quick preview
- Show key settings (alignment mode, location, guide rates)
- Show devices using this profile
- Show when profile was last used

**Mockup:**
```
┌────────────────────────────────────────────────────────────┐
│ Profile Management                        [+ New Profile]  │
├────────────────────────────────────────────────────────────┤
│ Search: [____________]  Mount Type: [All ▼]  Alignment: [All ▼] │
├────────────────────────────────────────────────────────────┤
│                                                             │
│ ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│ │ simulator-   │  │ simulator-   │  │ eq6-default  │      │
│ │ altaz        │  │ gem          │  │              │      │
│ │              │  │              │  │ 🔴 In Use    │      │
│ │ Mount: Sim   │  │ Mount: Sim   │  │ Mount: Sky   │      │
│ │ Align: AltAz │  │ Align: GEM   │  │ Align: GEM   │      │
│ │              │  │              │  │              │      │
│ │ [View] [Edit]│  │ [View] [Edit]│  │ [View] [Edit]│      │
│ │ [Delete] [...│  │ [Delete] [...│  │ [Delete] [...│      │
│ └──────────────┘  └──────────────┘  └──────────────┘      │
│                                                             │
│ ┌──────────────┐  ┌──────────────┐                        │
│ │ simulator-   │  │ az-gti-      │                        │
│ │ polar        │  │ default      │                        │
│ │              │  │              │                        │
│ │ Mount: Sim   │  │ Mount: Sky   │                        │
│ │ Align: Polar │  │ Align: AltAz │                        │
│ │              │  │              │                        │
│ │ [View] [Edit]│  │ [View] [Edit]│                        │
│ │ [Delete] [...│  │ [Delete] [...│                        │
│ └──────────────┘  └──────────────┘                        │
└────────────────────────────────────────────────────────────┘
```

---

### Feature 2: Profile Editor Component

**Description:** Form-based editor for creating/editing profiles

**User Story:**
> As a user, I want to edit profile settings in a form with validation, so I don't have to manually edit JSON files.

**Requirements:**

**FR2.1: Editor Modes**
- Create from template (select template first)
- Create from existing (select profile to copy)
- Create from scratch (empty form)
- Edit existing (load current values)

**FR2.2: Form Sections**
- **Basic Info:** Profile name, description, mount type
- **Location:** Latitude, longitude, elevation, timezone
- **Alignment:** Alignment mode, coordinate type
- **Hardware:** Serial port, baud rate, data bits (physical mounts only)
- **Tracking:** Tracking rate, sidereal rate, refraction
- **Guiding:** Guide rates, pulse minimums
- **Backlash:** RA backlash, Dec backlash
- **Optics:** Aperture diameter, focal length
- **Advanced:** Custom gearing, tracking offsets

**FR2.3: Validation**
- Required field validation (profile name, mount type)
- Range validation (latitude -90 to 90, longitude -180 to 180)
- Format validation (serial port COM1-COM99)
- Unique name validation (no duplicates)
- Real-time validation feedback
- Display errors inline with fields

**FR2.4: Save Actions**
- Save (save and return to browser)
- Save and Test (save and apply to simulator)
- Save and Apply (save and apply to selected device)
- Cancel (discard changes)

**FR2.5: Advanced Features**
- Toggle between form view and JSON view
- Import JSON (paste JSON directly)
- Export JSON (copy to clipboard)
- Reset to default values
- Undo/redo changes (form history)

**Mockup:**
```
┌────────────────────────────────────────────────────────────┐
│ Edit Profile: simulator-altaz                    [Form ▼]  │
├────────────────────────────────────────────────────────────┤
│                                                             │
│ Basic Information                                          │
│ ┌──────────────────────────────────────────────────────┐  │
│ │ Profile Name: [simulator-altaz_______________]       │  │
│ │ Description:  [AltAz simulator configuration________]│  │
│ │ Mount Type:   [Simulator ▼]                          │  │
│ └──────────────────────────────────────────────────────┘  │
│                                                             │
│ Location                                                   │
│ ┌──────────────────────────────────────────────────────┐  │
│ │ Latitude:     [51.21135_____] °                      │  │
│ │ Longitude:    [-1.459816____] °                      │  │
│ │ Elevation:    [10.0_________] m                      │  │
│ │ UTC Offset:   [00:00:00_____]                        │  │
│ └──────────────────────────────────────────────────────┘  │
│                                                             │
│ Alignment & Tracking                                       │
│ ┌──────────────────────────────────────────────────────┐  │
│ │ Alignment Mode: [AltAz ▼]                            │  │
│ │ ☑ Auto Track                                         │  │
│ │ Tracking Rate:  [Sidereal ▼]                         │  │
│ │ Sidereal Rate:  [15.0410671787] °/hour              │  │
│ │ ☑ Refraction Correction                              │  │
│ └──────────────────────────────────────────────────────┘  │
│                                                             │
│ [More Sections...] [▼]                                     │
│                                                             │
│ [Cancel]  [Save]  [Save and Test]  [Save and Apply to...] │
└────────────────────────────────────────────────────────────┘
```

---

### Feature 3: Runtime Profile Switching

**Description:** Change device profile without server restart (dynamic devices only)

**User Story:**
> As a user, I want to change a device's profile without restarting the server, so I can quickly test different configurations.

**Requirements:**

**FR3.1: Device Profile Change UI**
- Show "Change Profile" button on device card (dynamic devices only)
- Reserved slots show "Requires Restart" message
- Open profile selection dialog
- Show current profile
- List compatible profiles (same mount type)
- Preview selected profile before applying

**FR3.2: Profile Application**
- Validate new profile before applying
- Disconnect device if connected
- Load new profile settings
- Reinitialize device with new settings
- Reconnect device if was connected
- Show progress indicator during switch
- Show success/failure notification

**FR3.3: Rollback on Failure**
- If profile loading fails, revert to previous profile
- Show clear error message
- Log failure details for debugging
- Device remains in safe state (disconnected)

**FR3.4: Constraints**
- Only dynamic devices (device number ≥ 2)
- Cannot change reserved slots (0, 1) - requires restart
- Cannot change while device is connected (must disconnect first)
- Cannot change to invalid/incompatible profile

**Mockup:**
```
┌────────────────────────────────────────────────────────────┐
│ Device 2: Test Mount                                       │
├────────────────────────────────────────────────────────────┤
│ Current Profile: simulator-altaz                           │
│ Alignment Mode: AltAz                                      │
│ Status: Disconnected                                       │
│                                                             │
│ [Connect] [Change Profile] [Remove Device]                 │
└────────────────────────────────────────────────────────────┘
                       │
                       ▼ Click "Change Profile"
┌────────────────────────────────────────────────────────────┐
│ Change Profile for Device 2                          [×]   │
├────────────────────────────────────────────────────────────┤
│ Current: simulator-altaz (AltAz)                           │
│                                                             │
│ Select New Profile:                                        │
│ ○ simulator-gem (GermanPolar)                              │
│ ○ simulator-polar (Polar)                                  │
│ ○ eq6-default (GermanPolar)                                │
│                                                             │
│ Preview: simulator-gem                                     │
│ ┌──────────────────────────────────────────────────────┐  │
│ │ Mount Type: Simulator                                │  │
│ │ Alignment: GermanPolar                               │  │
│ │ Auto Track: Yes                                      │  │
│ │ RA Backlash: 25                                      │  │
│ │ Dec Backlash: 15                                     │  │
│ └──────────────────────────────────────────────────────┘  │
│                                                             │
│ ⚠️ This will disconnect and reinitialize the device.       │
│                                                             │
│ [Cancel]                                [Apply New Profile] │
└────────────────────────────────────────────────────────────┘
```

---

### Feature 4: Profile Templates

**Description:** Pre-configured profile starting points

**User Story:**
> As a user, I want to create a new profile from a template, so I don't have to configure everything from scratch.

**Requirements:**

**FR4.1: Built-in Templates**
- **Simulator Templates:**
  - AltAz Simulator (basic altitude-azimuth)
  - GEM Simulator (German Equatorial Mount with flip)
  - Polar Simulator (simple polar alignment)
  
- **Physical Mount Templates:**
  - SkyWatcher EQ6-Pro (GEM, high precision)
  - SkyWatcher AZ-GTi (AltAz, lightweight)
  - Celestron AVX (GEM, mid-range)
  - iOptron CEM120 (GEM, center-balanced)
  - Generic GEM (customizable starting point)
  - Generic AltAz (customizable starting point)

**FR4.2: Template Metadata**
- Template name
- Description (when to use this template)
- Mount type
- Typical use case
- Default values for all settings
- Required customizations (e.g., serial port)

**FR4.3: Template Selection**
- Show templates when creating new profile
- Group by category (Simulator, Physical Mount, Generic)
- Show description and preview
- Allow search/filter of templates
- Show "Popular" templates first

**FR4.4: Template Customization**
- Load template defaults into editor
- User customizes required fields (name, serial port, etc.)
- User optionally customizes other fields
- Save as new profile (template unchanged)

**Template Definitions (JSON):**
```json
{
  "templates": [
    {
      "id": "simulator-altaz",
      "name": "AltAz Simulator",
      "description": "Basic altitude-azimuth simulator for testing",
      "category": "simulator",
      "mountType": "Simulator",
      "popular": true,
      "requiredCustomizations": ["profileName"],
      "defaults": {
        "AlignmentMode": "AltAz",
        "AutoTrack": true,
        "TrackingRate": "Sidereal",
        "Latitude": 51.21135,
        "Longitude": -1.459816,
        "Elevation": 10.0,
        "...": "..."
      }
    },
    {
      "id": "eq6-pro",
      "name": "SkyWatcher EQ6-Pro",
      "description": "High-precision German Equatorial Mount",
      "category": "physical",
      "mountType": "SkyWatcher",
      "popular": true,
      "requiredCustomizations": ["profileName", "Port"],
      "defaults": {
        "AlignmentMode": "GermanPolar",
        "Port": "COM3",
        "BaudRate": 115200,
        "RaBacklash": 50,
        "DecBacklash": 35,
        "...": "..."
      }
    }
  ]
}
```

---

### Feature 5: Profile Validation

**Description:** Validate profile settings before save/apply

**User Story:**
> As a user, I want validation to prevent me from saving invalid settings, so my mount doesn't malfunction.

**Requirements:**

**FR5.1: Field-Level Validation**
- **Profile Name:**
  - Required
  - Unique (no duplicates)
  - Valid filename characters only
  - Max 50 characters
  
- **Location:**
  - Latitude: -90 to 90 degrees
  - Longitude: -180 to 180 degrees
  - Elevation: -500 to 10000 meters
  
- **Serial Port (Physical Mounts):**
  - Required for physical mounts
  - Format: COM1 to COM99 (Windows) or /dev/ttyUSB0 (Linux)
  
- **Baud Rate:**
  - Valid values: 9600, 19200, 38400, 57600, 115200
  
- **Guide Rates:**
  - Min: 0.1x sidereal
  - Max: 2.0x sidereal
  
- **Backlash:**
  - Min: 0 arcseconds
  - Max: 3600 arcseconds (1 degree)

**FR5.2: Profile-Level Validation**
- Mount type matches selected hardware
- Alignment mode compatible with mount type (e.g., AltAz mounts can't use GermanPolar)
- Required fields present based on mount type
- No conflicting settings

**FR5.3: JSON Validation**
- Valid JSON syntax
- Conforms to profile schema
- All property types correct (string, number, boolean)
- No extra/unknown properties (warn only)

**FR5.4: Validation Feedback**
- Real-time validation as user types
- Error messages displayed inline with fields
- Summary of all errors at top of form
- Prevent save if validation errors present
- Warning messages for non-critical issues (allow save)

**Validation Rules (Pseudo-Code):**
```csharp
public class ProfileValidator
{
    public ValidationResult Validate(ProfileModel profile)
    {
        var errors = new List<ValidationError>();
        
        // Profile name
        if (string.IsNullOrWhiteSpace(profile.ProfileName))
            errors.Add("Profile name is required");
        else if (profile.ProfileName.Length > 50)
            errors.Add("Profile name must be 50 characters or less");
        else if (!IsValidFileName(profile.ProfileName))
            errors.Add("Profile name contains invalid characters");
        else if (ProfileExists(profile.ProfileName))
            errors.Add("Profile name already exists");
        
        // Location
        if (profile.Latitude < -90 || profile.Latitude > 90)
            errors.Add("Latitude must be between -90 and 90 degrees");
        if (profile.Longitude < -180 || profile.Longitude > 180)
            errors.Add("Longitude must be between -180 and 180 degrees");
        
        // Physical mount requirements
        if (profile.MountType != "Simulator")
        {
            if (string.IsNullOrWhiteSpace(profile.Port))
                errors.Add("Serial port is required for physical mounts");
            if (!ValidBaudRates.Contains(profile.BaudRate))
                errors.Add($"Baud rate must be one of: {string.Join(", ", ValidBaudRates)}");
        }
        
        // Alignment mode compatibility
        if (profile.MountType == "AltAz" && profile.AlignmentMode == "GermanPolar")
            errors.Add("AltAz mounts cannot use GermanPolar alignment");
        
        return new ValidationResult(errors);
    }
}
```

---

### Feature 6: Profile Import/Export

**Description:** Backup, share, and restore profiles

**User Story:**
> As a user, I want to export my profiles for backup and import profiles shared by others, so I can preserve configurations and learn from the community.

**Requirements:**

**FR6.1: Export Profile**
- Export single profile as JSON file
- Export all profiles as ZIP archive
- Include metadata (export date, version)
- Option to include/exclude sensitive data (serial ports)
- Download to user's browser

**FR6.2: Import Profile**
- Import single profile from JSON file
- Import multiple profiles from ZIP archive
- Validate before importing
- Handle name conflicts (rename, overwrite, skip)
- Show import summary (success/failure)

**FR6.3: Export Format**
```json
{
  "export": {
    "version": "4.10",
    "date": "2025-01-XX",
    "profiles": [
      {
        "name": "my-eq6-config",
        "data": {
          "Mount": "SkyWatcher",
          "AlignmentMode": "GermanPolar",
          "...": "..."
        }
      }
    ]
  }
}
```

**FR6.4: Import Conflict Resolution**
```
┌────────────────────────────────────────────────────────────┐
│ Import Profiles                                      [×]   │
├────────────────────────────────────────────────────────────┤
│ File: my-profiles.zip                                      │
│                                                             │
│ Found 3 profiles:                                          │
│ ✅ my-eq6-config (new)                                     │
│ ⚠️ simulator-altaz (already exists)                        │
│    ○ Rename to: simulator-altaz-imported                   │
│    ○ Overwrite existing                                    │
│    ○ Skip this profile                                     │
│ ✅ custom-polar (new)                                      │
│                                                             │
│ [Cancel]                                    [Import (2/3)] │
└────────────────────────────────────────────────────────────┘
```

---

### Feature 7: Remote Client API Support

**Description:** REST API endpoints for remote client applications to download, upload, and manage profiles

**User Story:**
> As a remote client application developer, I want to download profiles from the server, allow users to edit them locally in my app, and upload them back to the server with server-side validation, so users can manage their telescope configurations from any application.

**Use Case Scenarios:**

**Scenario 1: Mobile Profile Editor**
- Mobile app connects to GreenSwamp server
- Downloads all available profiles
- User edits profile on tablet during setup
- Uploads updated profile to server
- Profile validated server-side before acceptance

**Scenario 2: Profile Management Tool**
- Desktop application for batch profile management
- Downloads all profiles for bulk editing
- Applies consistent settings across multiple profiles
- Uploads modified profiles back to server
- Server validates each profile before saving

**Scenario 3: Cloud Backup Service**
- Automated backup tool periodically downloads all profiles
- Stores profiles in cloud storage
- Can restore profiles by uploading back to server
- Server ensures restored profiles are valid

**Requirements:**

**FR7.1: Profile Download API**
- Download single profile by name (JSON)
- Download all profiles (bulk operation, JSON array)
- Download profile metadata only (list with summary info, no full settings)
- Support conditional download (If-Modified-Since header)
- Return profile version information

**FR7.2: Profile Upload API**
- Upload single profile (JSON)
- Upload multiple profiles (bulk operation, JSON array)
- **Mandatory server-side validation** before acceptance
- Return validation errors to client if profile is invalid
- Support profile creation or update (idempotent)
- Conflict resolution (overwrite flag, create-only flag)

**FR7.3: Profile Metadata API**
- List all available profiles with metadata (name, type, alignment, last modified, size)
- Get profile metadata by name (without full content)
- Check if profile exists (HEAD request)
- Get profiles modified since timestamp (incremental sync)

**FR7.4: Server-Side Validation** ⚠️ **CRITICAL**
- **All uploaded profiles MUST be validated server-side**
- Cannot trust client-side validation
- Validate JSON schema conformance
- Validate business rules (latitude range, required fields, etc.)
- Validate mount type compatibility
- Return detailed validation errors (field-level)
- Reject invalid profiles (HTTP 400 Bad Request)
- Log validation failures for security monitoring

**FR7.5: Security Considerations**
- File upload validation (max size limit: 1MB per profile)
- Path traversal prevention (profile name sanitization)
- Rate limiting on upload endpoints (prevent abuse)
- Authentication/authorization (if enabled in future phases)
- Audit logging for all create/update/delete operations

**FR7.6: API Response Formats**

**Profile List Response:**
```json
{
  "profiles": [
    {
      "name": "simulator-altaz",
      "displayName": "AltAz Simulator",
      "mountType": "Simulator",
      "alignmentMode": "AltAz",
      "lastModified": "2025-01-15T10:30:00Z",
      "sizeBytes": 4256,
      "inUse": false,
      "devices": []
    },
    {
      "name": "eq6-default",
      "displayName": "EQ6 Pro Default",
      "mountType": "SkyWatcher",
      "alignmentMode": "GermanPolar",
      "lastModified": "2025-01-15T09:15:00Z",
      "sizeBytes": 5128,
      "inUse": true,
      "devices": [0, 1]
    }
  ],
  "count": 2,
  "serverVersion": "4.10.0"
}
```

**Profile Download Response (Single):**
```json
{
  "name": "simulator-altaz",
  "profile": {
    "Mount": "Simulator",
    "AlignmentMode": "AltAz",
    "Latitude": 51.21135,
    "Longitude": -1.459816,
    ...
  },
  "metadata": {
    "lastModified": "2025-01-15T10:30:00Z",
    "version": "1.0",
    "checksum": "sha256:abc123..."
  }
}
```

**Profile Upload Request:**
```json
{
  "name": "my-custom-profile",
  "profile": {
    "Mount": "SkyWatcher",
    "AlignmentMode": "GermanPolar",
    ...
  },
  "options": {
    "overwrite": false,
    "validate": true
  }
}
```

**Validation Error Response:**
```json
{
  "isValid": false,
  "errors": [
    {
      "field": "Latitude",
      "message": "Latitude must be between -90 and 90 degrees",
      "value": "95.5"
    },
    {
      "field": "Port",
      "message": "Serial port is required for physical mounts",
      "value": null
    }
  ],
  "warnings": [
    {
      "field": "BaudRate",
      "message": "Baud rate 115200 is uncommon for SkyWatcher mounts, typically 9600",
      "value": "115200"
    }
  ]
}
```

**FR7.7: API Endpoints**

| Endpoint | Method | Purpose | Request | Response |
|----------|--------|---------|---------|----------|
| `/api/profiles` | GET | List all profiles with metadata | Query params: `modified_since`, `in_use` | Profile list (FR7.6) |
| `/api/profiles/{name}` | GET | Download single profile | - | Profile details (FR7.6) |
| `/api/profiles/{name}` | HEAD | Check if profile exists | - | 200 OK or 404 Not Found |
| `/api/profiles/{name}/metadata` | GET | Get profile metadata only | - | Metadata without full content |
| `/api/profiles/download/all` | GET | Download all profiles (bulk) | Query param: `format=json|zip` | JSON array or ZIP file |
| `/api/profiles/{name}` | PUT | Upload/update single profile | Profile JSON (FR7.6) | 200 OK or 400 Bad Request |
| `/api/profiles/upload/bulk` | POST | Upload multiple profiles | Array of profiles | Bulk upload result |
| `/api/profiles/{name}/validate` | POST | Validate profile without saving | Profile JSON | Validation result (FR7.6) |

**Note:** These endpoints are in addition to the existing `/setup/profiles` endpoints (which remain for backward compatibility and web UI).

**FR7.8: Client Library Support (Future)**
- Consider providing client SDK examples (C#, Python, JavaScript)
- Document API with OpenAPI/Swagger specification
- Provide example code for common operations
- Include error handling best practices

---

## UI/UX Design

### Navigation Structure

```
Main Navigation
│
├── Dashboard
│
├── Device Manager  (existing)
│   └── Device List (with "Change Profile" button added)
│
├── Profile Management  (NEW)
│   ├── Profile Browser (list/grid view)
│   ├── Profile Editor (create/edit form)
│   └── Profile Templates (template selection)
│
└── Setup
    └── ... (existing pages)
```

### Page Hierarchy

```
/profiles                    → Profile Browser Page
/profiles/new                → Create New Profile (template selection)
/profiles/new/{template}     → Create from Template (editor with defaults)
/profiles/{name}             → View Profile (read-only)
/profiles/{name}/edit        → Edit Profile (editor form)
/profiles/import             → Import Profiles (upload UI)
```

### Component Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ ProfileBrowser.razor (Page)                                 │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ ProfileSearchBar.razor (Component)                     │ │
│  │ - Search input                                         │ │
│  │ - Filter dropdowns                                     │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ ProfileCardGrid.razor (Component)                      │ │
│  │                                                         │ │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐│ │
│  │  │ ProfileCard  │  │ ProfileCard  │  │ ProfileCard  ││ │
│  │  │.razor        │  │.razor        │  │.razor        ││ │
│  │  └──────────────┘  └──────────────┘  └──────────────┘│ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ ProfilePreviewModal.razor (Component)                  │ │
│  │ - Show when card clicked                               │ │
│  │ - Read-only profile details                            │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ ProfileEditor.razor (Page)                                  │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ ProfileEditorForm.razor (Component)                    │ │
│  │                                                         │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │ BasicInfoSection.razor                           │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │ LocationSection.razor                            │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │ AlignmentSection.razor                           │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │ HardwareSection.razor (conditional)              │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │  ... (more sections)                                   │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ ValidationSummary.razor (Component)                    │ │
│  │ - Display errors at top                                │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### Design System

**Colors:**
- Primary: #007bff (existing Blazor default)
- Success: #28a745 (profile saved, imported)
- Warning: #ffc107 (validation warnings, conflicts)
- Danger: #dc3545 (errors, delete confirmation)
- Info: #17a2b8 (tips, help text)

**Typography:**
- Headers: Segoe UI (existing)
- Body: Segoe UI, 14px
- Code/JSON: Consolas, 12px (monospace)

**Spacing:**
- Card padding: 16px
- Section spacing: 24px
- Form field spacing: 12px

**Icons:**
- Use Open Iconic (existing in project)
- Edit: ✏️ (pencil)
- Delete: 🗑️ (trash)
- View: 👁️ (eye)
- Download: ⬇️ (download)
- Upload: ⬆️ (upload)
- Copy: 📋 (clipboard)

---

## Implementation Tasks

### Task 1: Extend Profile Service (Backend)

**Priority:** 🔴 Critical  
**Estimated Time:** 2-3 hours  
**Dependencies:** None

**Objective:** Add missing methods to `ISettingsProfileService` for profile management

#### Step 1.1: Update Interface

**File:** `GreenSwamp.Alpaca.Settings/Services/ISettingsProfileService.cs`

**Add Methods:**
```csharp
public interface ISettingsProfileService
{
    // Existing methods (Phase 4.9):
    Task LoadProfileByNameAsync(string profileName, SkySettingsInstance instance);
    Task<IEnumerable<string>> GetProfileNamesAsync();
    bool ProfileExists(string profileName);
    
    // NEW for Phase 4.10:
    
    /// <summary>
    /// Gets full profile details including all settings.
    /// </summary>
    Task<ProfileModel> GetProfileDetailsAsync(string profileName);
    
    /// <summary>
    /// Saves a new or updated profile.
    /// </summary>
    /// <param name="profileName">Name of profile (filename without .json)</param>
    /// <param name="profile">Profile data to save</param>
    /// <param name="overwrite">If true, overwrite existing profile</param>
    Task SaveProfileAsync(string profileName, ProfileModel profile, bool overwrite = false);
    
    /// <summary>
    /// Deletes a profile.
    /// </summary>
    /// <param name="profileName">Name of profile to delete</param>
    /// <param name="force">If true, delete even if in use</param>
    Task DeleteProfileAsync(string profileName, bool force = false);
    
    /// <summary>
    /// Validates profile data against schema and business rules.
    /// </summary>
    Task<ValidationResult> ValidateProfileAsync(ProfileModel profile);
    
    /// <summary>
    /// Gets list of available profile templates.
    /// </summary>
    Task<IEnumerable<ProfileTemplate>> GetTemplatesAsync();
    
    /// <summary>
    /// Creates a new profile from a template.
    /// </summary>
    Task<ProfileModel> CreateFromTemplateAsync(string templateId);
    
    /// <summary>
    /// Checks if a profile is currently in use by any device.
    /// </summary>
    Task<bool> IsProfileInUseAsync(string profileName);
    
    /// <summary>
    /// Gets list of devices using a profile.
    /// </summary>
    Task<IEnumerable<int>> GetDevicesUsingProfileAsync(string profileName);
}
```

#### Step 1.2: Implement New Methods

**File:** `GreenSwamp.Alpaca.Settings/Services/SettingsProfileService.cs`

**Implementation approach:**
- `GetProfileDetailsAsync()`: Read JSON file, deserialize to ProfileModel
- `SaveProfileAsync()`: Serialize ProfileModel to JSON, write to file
- `DeleteProfileAsync()`: Check if in use, delete file
- `ValidateProfileAsync()`: Use JSON schema, custom validators
- `GetTemplatesAsync()`: Read templates.json, return list
- `CreateFromTemplateAsync()`: Load template, merge defaults
- `IsProfileInUseAsync()`: Query device registry
- `GetDevicesUsingProfileAsync()`: Query device registry, filter by profile

**Estimated Lines:** 400-500 lines

#### Step 1.3: Create Profile Models

**File:** `GreenSwamp.Alpaca.Settings/Models/ProfileModel.cs`

```csharp
public class ProfileModel
{
    public string ProfileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Mount { get; set; } = "Simulator";
    public string AlignmentMode { get; set; } = "GermanPolar";
    
    // Location
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Elevation { get; set; }
    public string UTCOffset { get; set; } = "00:00:00";
    
    // Hardware (physical mounts only)
    public string? Port { get; set; }
    public int? BaudRate { get; set; }
    public int? DataBits { get; set; }
    public string? Handshake { get; set; }
    
    // Tracking
    public bool AutoTrack { get; set; }
    public string TrackingRate { get; set; } = "Sidereal";
    public double SiderealRate { get; set; } = 15.0410671787;
    public bool Refraction { get; set; }
    
    // Guiding
    public double St4Guiderate { get; set; }
    public double GuideRateOffsetX { get; set; }
    public double GuideRateOffsetY { get; set; }
    public int MinPulseRa { get; set; }
    public int MinPulseDec { get; set; }
    
    // Backlash
    public int RaBacklash { get; set; }
    public int DecBacklash { get; set; }
    
    // Optics
    public double ApertureDiameter { get; set; }
    public double ApertureArea { get; set; }
    public double FocalLength { get; set; }
    
    // ... (all other settings)
}

public class ProfileTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Generic";
    public string MountType { get; set; } = "Simulator";
    public bool Popular { get; set; }
    public List<string> RequiredCustomizations { get; set; } = new();
    public ProfileModel Defaults { get; set; } = new();
}

public class ValidationResult
{
    public bool IsValid => !Errors.Any();
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
}

public class ValidationError
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ValidationWarning
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
```

**Acceptance Criteria:**
- ✅ All methods implemented
- ✅ Unit tests pass
- ✅ Profile CRUD operations working
- ✅ Validation catching common errors

---

### Task 2: Add REST API Endpoints (Backend)

**Priority:** 🔴 Critical  
**Estimated Time:** 3-4 hours (increased from 2-3h to account for remote client API controller)  
**Dependencies:** Task 1

**Objective:** Add REST API endpoints for profile management (web UI + remote client support)

#### Step 2.1: Extend SetupDevicesController

**File:** `GreenSwamp.Alpaca.Server/Controllers/SetupDevicesController.cs`

**Add Endpoints:**

**1. GET /setup/profiles/{name} - Get Profile Details**
```csharp
[HttpGet("profiles/{name}")]
[AllowAnonymous]
[ProducesResponseType(typeof(ProfileModel), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetProfile(string name)
{
    try
    {
        if (_profileService == null)
            return NotFound(new ErrorResponse { Error = "Profile service not available" });
        
        if (!_profileService.ProfileExists(name))
            return NotFound(new ErrorResponse { Error = $"Profile '{name}' not found" });
        
        var profile = await _profileService.GetProfileDetailsAsync(name);
        return Ok(profile);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get profile {ProfileName}", name);
        return StatusCode(500, new ErrorResponse { Error = ex.Message });
    }
}
```

**2. POST /setup/profiles - Create Profile**
```csharp
[HttpPost("profiles")]
[AllowAnonymous]
[ProducesResponseType(typeof(ProfileModel), StatusCodes.Status201Created)]
[ProducesResponseType(typeof(ValidationResult), StatusCodes.Status400BadRequest)]
public async Task<IActionResult> CreateProfile([FromBody] ProfileModel profile)
{
    try
    {
        // Validate
        var validation = await _profileService.ValidateProfileAsync(profile);
        if (!validation.IsValid)
            return BadRequest(validation);
        
        // Check for duplicates
        if (_profileService.ProfileExists(profile.ProfileName))
            return Conflict(new ErrorResponse { Error = $"Profile '{profile.ProfileName}' already exists" });
        
        // Save
        await _profileService.SaveProfileAsync(profile.ProfileName, profile, overwrite: false);
        
        _logger.LogInformation("Created profile {ProfileName}", profile.ProfileName);
        return CreatedAtAction(nameof(GetProfile), new { name = profile.ProfileName }, profile);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to create profile");
        return StatusCode(500, new ErrorResponse { Error = ex.Message });
    }
}
```

**3. PUT /setup/profiles/{name} - Update Profile**
```csharp
[HttpPut("profiles/{name}")]
[AllowAnonymous]
[ProducesResponseType(typeof(ProfileModel), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ValidationResult), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public async Task<IActionResult> UpdateProfile(string name, [FromBody] ProfileModel profile)
{
    // Similar to CreateProfile but with overwrite: true
}
```

**4. DELETE /setup/profiles/{name} - Delete Profile**
```csharp
[HttpDelete("profiles/{name}")]
[AllowAnonymous]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
public async Task<IActionResult> DeleteProfile(string name, [FromQuery] bool force = false)
{
    try
    {
        if (!_profileService.ProfileExists(name))
            return NotFound(new ErrorResponse { Error = $"Profile '{name}' not found" });
        
        // Check if in use
        if (!force && await _profileService.IsProfileInUseAsync(name))
        {
            var devices = await _profileService.GetDevicesUsingProfileAsync(name);
            return Conflict(new ErrorResponse 
            { 
                Error = $"Profile '{name}' is in use by devices: {string.Join(", ", devices)}. Use force=true to delete anyway." 
            });
        }
        
        await _profileService.DeleteProfileAsync(name, force);
        
        _logger.LogInformation("Deleted profile {ProfileName}", name);
        return NoContent();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to delete profile {ProfileName}", name);
        return StatusCode(500, new ErrorResponse { Error = ex.Message });
    }
}
```

**5. POST /setup/profiles/{name}/validate - Validate Profile**
```csharp
[HttpPost("profiles/{name}/validate")]
[AllowAnonymous]
[ProducesResponseType(typeof(ValidationResult), StatusCodes.Status200OK)]
public async Task<IActionResult> ValidateProfile(string name, [FromBody] ProfileModel profile)
{
    var validation = await _profileService.ValidateProfileAsync(profile);
    return Ok(validation);
}
```

**6. PUT /setup/devices/{id}/profile - Change Device Profile**
```csharp
[HttpPut("devices/{id}/profile")]
[AllowAnonymous]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public async Task<IActionResult> ChangeDeviceProfile(int id, [FromBody] ChangeProfileRequest request)
{
    try
    {
        // Validate device exists
        var device = UnifiedDeviceRegistry.GetDeviceInfo(id);
        if (device == null)
            return NotFound(new ErrorResponse { Error = $"Device {id} not found" });
        
        // Check if reserved slot
        if (device.IsReserved)
            return BadRequest(new ErrorResponse 
            { 
                Error = "Cannot change profile for reserved slots. Requires server restart." 
            });
        
        // Check if device is connected
        if (device.IsConnected)
            return BadRequest(new ErrorResponse 
            { 
                Error = "Device must be disconnected before changing profile" 
            });
        
        // Validate new profile exists
        if (!_profileService.ProfileExists(request.ProfileName))
            return NotFound(new ErrorResponse { Error = $"Profile '{request.ProfileName}' not found" });
        
        // Validate new profile
        var profile = await _profileService.GetProfileDetailsAsync(request.ProfileName);
        var validation = await _profileService.ValidateProfileAsync(profile);
        if (!validation.IsValid)
            return BadRequest(validation);
        
        // Get current settings instance
        var instance = MountInstanceRegistry.GetInstance(id);
        if (instance == null)
            return NotFound(new ErrorResponse { Error = $"Mount instance for device {id} not found" });
        
        // Load new profile into instance
        await _profileService.LoadProfileByNameAsync(request.ProfileName, instance.Settings);
        
        // Update device configuration
        device.ProfileName = request.ProfileName;
        
        _logger.LogInformation("Changed device {DeviceNumber} profile to {ProfileName}", id, request.ProfileName);
        return Ok(new { message = $"Device {id} profile changed to '{request.ProfileName}'" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to change device profile");
        return StatusCode(500, new ErrorResponse { Error = ex.Message });
    }
}

public class ChangeProfileRequest
{
    public string ProfileName { get; set; } = string.Empty;
}
```

**7. GET /setup/profiles/templates - List Templates**
```csharp
[HttpGet("profiles/templates")]
[AllowAnonymous]
[ProducesResponseType(typeof(IEnumerable<ProfileTemplate>), StatusCodes.Status200OK)]
public async Task<IActionResult> GetTemplates()
{
    var templates = await _profileService.GetTemplatesAsync();
    return Ok(templates);
}
```

#### Step 2.2: Create Remote Client API Controller

**File:** `GreenSwamp.Alpaca.Server/Controllers/ProfileApiController.cs` (NEW FILE)

**Purpose:** Dedicated REST API controller for remote client applications

**Rationale:**
- Separate `/api/profiles` endpoints from `/setup/profiles` (web UI)
- Optimized response formats for programmatic access
- Support bulk operations (download all, upload multiple)
- Enhanced metadata endpoints for efficient client sync
- Consistent API design for third-party integration

**Controller Implementation:**

```csharp
using Microsoft.AspNetCore.Mvc;
using GreenSwamp.Alpaca.Settings.Services;
using GreenSwamp.Alpaca.Settings.Models;

namespace GreenSwamp.Alpaca.Server.Controllers;

/// <summary>
/// REST API for remote client applications to manage profiles.
/// </summary>
[ApiController]
[Route("api/profiles")]
[AllowAnonymous] // TODO: Add authentication in future phase
public class ProfileApiController : ControllerBase
{
    private readonly ISettingsProfileService _profileService;
    private readonly ILogger<ProfileApiController> _logger;
    private const int MaxProfileSizeBytes = 1_048_576; // 1MB limit

    public ProfileApiController(
        ISettingsProfileService profileService,
        ILogger<ProfileApiController> logger)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// GET /api/profiles - List all profiles with metadata
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ProfileListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProfiles(
        [FromQuery] DateTime? modifiedSince = null,
        [FromQuery] bool? inUse = null)
    {
        try
        {
            var profileNames = await _profileService.GetProfileNamesAsync();
            var profiles = new List<ProfileMetadata>();

            foreach (var name in profileNames)
            {
                var profile = await _profileService.GetProfileDetailsAsync(name);
                var metadata = new ProfileMetadata
                {
                    Name = name,
                    DisplayName = profile.DisplayName ?? name,
                    MountType = profile.Mount,
                    AlignmentMode = profile.AlignmentMode,
                    LastModified = profile.LastModified ?? DateTime.UtcNow,
                    SizeBytes = EstimateProfileSize(profile),
                    InUse = await _profileService.IsProfileInUseAsync(name),
                    Devices = (await _profileService.GetDevicesUsingProfileAsync(name)).ToList()
                };

                // Filter by modifiedSince
                if (modifiedSince.HasValue && metadata.LastModified < modifiedSince.Value)
                    continue;

                // Filter by inUse
                if (inUse.HasValue && metadata.InUse != inUse.Value)
                    continue;

                profiles.Add(metadata);
            }

            var response = new ProfileListResponse
            {
                Profiles = profiles,
                Count = profiles.Count,
                ServerVersion = GetServerVersion()
            };

            _logger.LogInformation("Listed {Count} profiles for remote client", profiles.Count);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list profiles for remote client");
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/profiles/{name} - Download single profile
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ProfileDownloadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadProfile(string name)
    {
        try
        {
            if (!_profileService.ProfileExists(name))
                return NotFound(new ErrorResponse { Error = $"Profile '{name}' not found" });

            var profile = await _profileService.GetProfileDetailsAsync(name);

            var response = new ProfileDownloadResponse
            {
                Name = name,
                Profile = profile,
                Metadata = new ProfileMetadata
                {
                    Name = name,
                    LastModified = profile.LastModified ?? DateTime.UtcNow,
                    SizeBytes = EstimateProfileSize(profile),
                    Checksum = ComputeChecksum(profile)
                }
            };

            _logger.LogInformation("Profile '{ProfileName}' downloaded by remote client", name);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download profile {ProfileName}", name);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// HEAD /api/profiles/{name} - Check if profile exists
    /// </summary>
    [HttpHead("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult CheckProfileExists(string name)
    {
        return _profileService.ProfileExists(name) ? Ok() : NotFound();
    }

    /// <summary>
    /// GET /api/profiles/{name}/metadata - Get metadata without full content
    /// </summary>
    [HttpGet("{name}/metadata")]
    [ProducesResponseType(typeof(ProfileMetadata), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfileMetadata(string name)
    {
        try
        {
            if (!_profileService.ProfileExists(name))
                return NotFound(new ErrorResponse { Error = $"Profile '{name}' not found" });

            var profile = await _profileService.GetProfileDetailsAsync(name);

            var metadata = new ProfileMetadata
            {
                Name = name,
                DisplayName = profile.DisplayName ?? name,
                MountType = profile.Mount,
                AlignmentMode = profile.AlignmentMode,
                LastModified = profile.LastModified ?? DateTime.UtcNow,
                SizeBytes = EstimateProfileSize(profile),
                InUse = await _profileService.IsProfileInUseAsync(name),
                Devices = (await _profileService.GetDevicesUsingProfileAsync(name)).ToList()
            };

            return Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metadata for profile {ProfileName}", name);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/profiles/download/all - Download all profiles (bulk)
    /// </summary>
    [HttpGet("download/all")]
    [ProducesResponseType(typeof(BulkProfileDownloadResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadAllProfiles([FromQuery] string format = "json")
    {
        try
        {
            var profileNames = await _profileService.GetProfileNamesAsync();
            var profiles = new List<ProfileDownloadResponse>();

            foreach (var name in profileNames)
            {
                var profile = await _profileService.GetProfileDetailsAsync(name);
                profiles.Add(new ProfileDownloadResponse
                {
                    Name = name,
                    Profile = profile,
                    Metadata = new ProfileMetadata
                    {
                        Name = name,
                        LastModified = profile.LastModified ?? DateTime.UtcNow
                    }
                });
            }

            if (format.ToLower() == "zip")
            {
                // TODO: Implement ZIP export in future iteration
                return BadRequest(new ErrorResponse { Error = "ZIP format not yet implemented. Use format=json" });
            }

            var response = new BulkProfileDownloadResponse
            {
                Profiles = profiles,
                Count = profiles.Count,
                ExportDate = DateTime.UtcNow,
                ServerVersion = GetServerVersion()
            };

            _logger.LogInformation("Downloaded all profiles ({Count}) for remote client", profiles.Count);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download all profiles");
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// PUT /api/profiles/{name} - Upload/update profile with validation
    /// </summary>
    [HttpPut("{name}")]
    [ProducesResponseType(typeof(ProfileUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadProfile(string name, [FromBody] ProfileUploadRequest request)
    {
        try
        {
            // Security: Validate profile size
            var profileSize = EstimateProfileSize(request.Profile);
            if (profileSize > MaxProfileSizeBytes)
            {
                return BadRequest(new ErrorResponse 
                { 
                    Error = $"Profile size ({profileSize} bytes) exceeds maximum allowed ({MaxProfileSizeBytes} bytes)" 
                });
            }

            // Security: Sanitize profile name (prevent path traversal)
            if (!IsValidProfileName(name))
            {
                return BadRequest(new ErrorResponse 
                { 
                    Error = "Invalid profile name. Only alphanumeric, dash, and underscore characters allowed." 
                });
            }

            // CRITICAL: Server-side validation (cannot trust client)
            var validation = await _profileService.ValidateProfileAsync(request.Profile);
            if (!validation.IsValid)
            {
                _logger.LogWarning("Profile upload rejected due to validation errors: {ProfileName}", name);
                return BadRequest(validation);
            }

            // Check if profile exists
            bool profileExists = _profileService.ProfileExists(name);

            if (profileExists && !request.Options.Overwrite)
            {
                return Conflict(new ErrorResponse 
                { 
                    Error = $"Profile '{name}' already exists. Set overwrite=true to update." 
                });
            }

            // Save profile
            await _profileService.SaveProfileAsync(name, request.Profile, overwrite: request.Options.Overwrite);

            var response = new ProfileUploadResponse
            {
                Success = true,
                ProfileName = name,
                Action = profileExists ? "updated" : "created",
                Message = $"Profile '{name}' {(profileExists ? "updated" : "created")} successfully"
            };

            _logger.LogInformation(
                "Profile '{ProfileName}' {Action} by remote client from {RemoteIp}", 
                name, 
                response.Action,
                HttpContext.Connection.RemoteIpAddress);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload profile {ProfileName}", name);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/profiles/upload/bulk - Upload multiple profiles
    /// </summary>
    [HttpPost("upload/bulk")]
    [ProducesResponseType(typeof(BulkProfileUploadResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadBulkProfiles([FromBody] BulkProfileUploadRequest request)
    {
        var results = new List<ProfileUploadResult>();

        foreach (var profileRequest in request.Profiles)
        {
            try
            {
                // Validate
                var validation = await _profileService.ValidateProfileAsync(profileRequest.Profile);

                if (!validation.IsValid)
                {
                    results.Add(new ProfileUploadResult
                    {
                        Name = profileRequest.Name,
                        Success = false,
                        Message = "Validation failed",
                        Errors = validation.Errors.Select(e => e.Message).ToList()
                    });
                    continue;
                }

                // Save
                bool exists = _profileService.ProfileExists(profileRequest.Name);
                await _profileService.SaveProfileAsync(
                    profileRequest.Name, 
                    profileRequest.Profile, 
                    overwrite: request.Options.Overwrite);

                results.Add(new ProfileUploadResult
                {
                    Name = profileRequest.Name,
                    Success = true,
                    Message = $"Profile {(exists ? "updated" : "created")} successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload profile {ProfileName} in bulk operation", profileRequest.Name);
                results.Add(new ProfileUploadResult
                {
                    Name = profileRequest.Name,
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        var response = new BulkProfileUploadResponse
        {
            Results = results,
            TotalCount = request.Profiles.Count,
            SuccessCount = results.Count(r => r.Success),
            FailureCount = results.Count(r => !r.Success)
        };

        _logger.LogInformation(
            "Bulk profile upload completed: {SuccessCount}/{TotalCount} successful", 
            response.SuccessCount, 
            response.TotalCount);

        return Ok(response);
    }

    /// <summary>
    /// POST /api/profiles/{name}/validate - Validate profile without saving
    /// </summary>
    [HttpPost("{name}/validate")]
    [ProducesResponseType(typeof(ValidationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateProfile(string name, [FromBody] ProfileModel profile)
    {
        var validation = await _profileService.ValidateProfileAsync(profile);

        _logger.LogInformation(
            "Profile '{ProfileName}' validated by remote client: {IsValid}", 
            name, 
            validation.IsValid);

        return Ok(validation);
    }

    // Helper methods
    private long EstimateProfileSize(ProfileModel profile)
    {
        // Rough estimate: serialize to JSON and get byte count
        var json = System.Text.Json.JsonSerializer.Serialize(profile);
        return System.Text.Encoding.UTF8.GetByteCount(json);
    }

    private string ComputeChecksum(ProfileModel profile)
    {
        // Simple checksum for now (can enhance to SHA256)
        var json = System.Text.Json.JsonSerializer.Serialize(profile);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return "sha256:" + BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    private bool IsValidProfileName(string name)
    {
        // Only allow alphanumeric, dash, underscore (prevent path traversal)
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$");
    }

    private string GetServerVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        return version?.ToString() ?? "4.10.0";
    }
}

// Request/Response Models
public class ProfileUploadRequest
{
    public ProfileModel Profile { get; set; } = new();
    public UploadOptions Options { get; set; } = new();
}

public class UploadOptions
{
    public bool Overwrite { get; set; } = false;
    public bool Validate { get; set; } = true; // Always validates regardless
}

public class ProfileUploadResponse
{
    public bool Success { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "created" or "updated"
    public string Message { get; set; } = string.Empty;
}

public class BulkProfileUploadRequest
{
    public List<ProfileUploadItem> Profiles { get; set; } = new();
    public UploadOptions Options { get; set; } = new();
}

public class ProfileUploadItem
{
    public string Name { get; set; } = string.Empty;
    public ProfileModel Profile { get; set; } = new();
}

public class BulkProfileUploadResponse
{
    public List<ProfileUploadResult> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}

public class ProfileUploadResult
{
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

public class ProfileListResponse
{
    public List<ProfileMetadata> Profiles { get; set; } = new();
    public int Count { get; set; }
    public string ServerVersion { get; set; } = string.Empty;
}

public class ProfileMetadata
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MountType { get; set; } = string.Empty;
    public string AlignmentMode { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long SizeBytes { get; set; }
    public bool InUse { get; set; }
    public List<int> Devices { get; set; } = new();
    public string? Checksum { get; set; }
}

public class ProfileDownloadResponse
{
    public string Name { get; set; } = string.Empty;
    public ProfileModel Profile { get; set; } = new();
    public ProfileMetadata Metadata { get; set; } = new();
}

public class BulkProfileDownloadResponse
{
    public List<ProfileDownloadResponse> Profiles { get; set; } = new();
    public int Count { get; set; }
    public DateTime ExportDate { get; set; }
    public string ServerVersion { get; set; } = string.Empty;
}
```

**Security Considerations:**
- ✅ Profile size limit enforced (1MB max)
- ✅ Profile name sanitization (prevent path traversal attacks)
- ✅ Server-side validation mandatory (cannot trust client)
- ✅ Audit logging for all create/update/delete operations
- ✅ Remote IP logging for security monitoring
- 🔜 Authentication/authorization (future phase)
- 🔜 Rate limiting (future phase)

**Acceptance Criteria:**
- ✅ All endpoints implemented in SetupDevicesController and ProfileApiController
- ✅ API documentation updated (Swagger)
- ✅ Error handling comprehensive
- ✅ Logging added
- ✅ Integration tests pass
- ✅ Remote client endpoints support bulk operations
- ✅ Server-side validation enforced on all uploads
- ✅ Security controls implemented (size limits, name sanitization)

---

### Task 3: Profile Browser Page (Frontend)

**Priority:** 🟡 High  
**Estimated Time:** 3-4 hours  
**Dependencies:** Task 2

**Objective:** Create main profile management page

#### Step 3.1: Create ProfileBrowser.razor

**File:** `GreenSwamp.Alpaca.Server/Pages/Profiles/ProfileBrowser.razor`

**Structure:**
```razor
@page "/profiles"
@using GreenSwamp.Alpaca.Settings.Models
@inject HttpClient Http
@inject NavigationManager NavManager

<PageTitle>Profile Management</PageTitle>

<h3>Profile Management</h3>

<div class="profile-browser">
    <!-- Search and Filter Bar -->
    <ProfileSearchBar 
        SearchText="@searchText" 
        MountTypeFilter="@mountTypeFilter" 
        AlignmentModeFilter="@alignmentModeFilter"
        OnSearch="HandleSearch" 
        OnFilterChange="HandleFilterChange" />
    
    <!-- Action Buttons -->
    <div class="action-bar">
        <button class="btn btn-primary" @onclick="CreateNewProfile">
            <span class="oi oi-plus"></span> New Profile
        </button>
        <button class="btn btn-secondary" @onclick="ImportProfiles">
            <span class="oi oi-data-transfer-upload"></span> Import
        </button>
        <button class="btn btn-secondary" @onclick="ExportAllProfiles">
            <span class="oi oi-data-transfer-download"></span> Export All
        </button>
    </div>
    
    <!-- Profile Grid -->
    @if (profiles == null)
    {
        <p><em>Loading profiles...</em></p>
    }
    else if (!profiles.Any())
    {
        <div class="alert alert-info">
            No profiles found. <a href="/profiles/new">Create your first profile</a>
        </div>
    }
    else
    {
        <ProfileCardGrid 
            Profiles="@filteredProfiles" 
            OnView="ViewProfile"
            OnEdit="EditProfile"
            OnDelete="DeleteProfile"
            OnDuplicate="DuplicateProfile"
            OnExport="ExportProfile" />
    }
    
    <!-- Preview Modal -->
    @if (selectedProfile != null)
    {
        <ProfilePreviewModal 
            Profile="@selectedProfile" 
            IsOpen="@showPreview"
            OnClose="ClosePreview" />
    }
    
    <!-- Delete Confirmation Modal -->
    @if (profileToDelete != null)
    {
        <ConfirmDialog 
            Title="Delete Profile"
            Message="@GetDeleteMessage()"
            OnConfirm="ConfirmDelete"
            OnCancel="CancelDelete" />
    }
</div>

@code {
    private List<ProfileInfo>? profiles;
    private List<ProfileInfo> filteredProfiles => FilterProfiles();
    
    private string searchText = string.Empty;
    private string mountTypeFilter = "All";
    private string alignmentModeFilter = "All";
    
    private ProfileModel? selectedProfile;
    private bool showPreview = false;
    
    private string? profileToDelete;
    
    protected override async Task OnInitializedAsync()
    {
        await LoadProfiles();
    }
    
    private async Task LoadProfiles()
    {
        try
        {
            profiles = await Http.GetFromJsonAsync<List<ProfileInfo>>("/setup/profiles");
        }
        catch (Exception ex)
        {
            // Handle error
            Console.WriteLine($"Failed to load profiles: {ex.Message}");
        }
    }
    
    private List<ProfileInfo> FilterProfiles()
    {
        if (profiles == null) return new List<ProfileInfo>();
        
        var filtered = profiles.AsEnumerable();
        
        // Search by name
        if (!string.IsNullOrWhiteSpace(searchText))
            filtered = filtered.Where(p => p.ProfileName.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        
        // Filter by mount type
        if (mountTypeFilter != "All")
            filtered = filtered.Where(p => p.MountType == mountTypeFilter);
        
        // Filter by alignment mode
        if (alignmentModeFilter != "All")
            filtered = filtered.Where(p => p.AlignmentMode == alignmentModeFilter);
        
        return filtered.ToList();
    }
    
    private void HandleSearch(string searchText)
    {
        this.searchText = searchText;
    }
    
    private void HandleFilterChange(string mountType, string alignmentMode)
    {
        mountTypeFilter = mountType;
        alignmentModeFilter = alignmentMode;
    }
    
    private void CreateNewProfile()
    {
        NavManager.NavigateTo("/profiles/new");
    }
    
    private void ViewProfile(string profileName)
    {
        NavManager.NavigateTo($"/profiles/{profileName}");
    }
    
    private void EditProfile(string profileName)
    {
        NavManager.NavigateTo($"/profiles/{profileName}/edit");
    }
    
    private void DeleteProfile(string profileName)
    {
        profileToDelete = profileName;
    }
    
    private async Task ConfirmDelete()
    {
        if (profileToDelete == null) return;
        
        try
        {
            var response = await Http.DeleteAsync($"/setup/profiles/{profileToDelete}");
            if (response.IsSuccessStatusCode)
            {
                await LoadProfiles();
                profileToDelete = null;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Profile in use - show force delete option
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                // Show error dialog with force option
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete profile: {ex.Message}");
        }
    }
    
    private void CancelDelete()
    {
        profileToDelete = null;
    }
    
    private void DuplicateProfile(string profileName)
    {
        NavManager.NavigateTo($"/profiles/new?copy={profileName}");
    }
    
    private void ExportProfile(string profileName)
    {
        // Trigger file download
        NavManager.NavigateTo($"/setup/profiles/{profileName}/export", forceLoad: true);
    }
    
    private void ImportProfiles()
    {
        NavManager.NavigateTo("/profiles/import");
    }
    
    private void ExportAllProfiles()
    {
        // Trigger ZIP download
        NavManager.NavigateTo("/setup/profiles/export/all", forceLoad: true);
    }
    
    private string GetDeleteMessage()
    {
        return $"Are you sure you want to delete profile '{profileToDelete}'? This action cannot be undone.";
    }
}
```

#### Step 3.2: Create Supporting Components

**Components to Create:**
1. `ProfileSearchBar.razor` - Search and filter controls
2. `ProfileCardGrid.razor` - Grid of profile cards
3. `ProfileCard.razor` - Individual profile card
4. `ProfilePreviewModal.razor` - Quick view modal
5. `ConfirmDialog.razor` - Reusable confirmation dialog

**Estimated Lines:** 800-1000 lines total

---

### Task 4: Profile Editor Page (Frontend)

**Priority:** 🟡 High  
**Estimated Time:** 4-5 hours  
**Dependencies:** Task 2, Task 3

**Objective:** Create form-based profile editor

*(Details similar to Task 3 structure - comprehensive form with sections, validation, save handlers)*

---

### Task 5: Runtime Profile Switching (Frontend + Backend Integration)

**Priority:** 🟢 Medium  
**Estimated Time:** 2-3 hours  
**Dependencies:** Task 2, Task 4

**Objective:** Add "Change Profile" feature to Device Manager

*(Details for adding button to DeviceManager.razor, modal for profile selection, API integration)*

---

### Task 6: Profile Templates System

**Priority:** 🟢 Medium  
**Estimated Time:** 2-3 hours  
**Dependencies:** Task 1

**Objective:** Implement profile templates

*(Details for templates.json file, template selection UI, template defaults application)*

---

### Task 7: Testing and Documentation

**Priority:** 🟡 High  
**Estimated Time:** 2 hours  
**Dependencies:** All previous tasks

**Objective:** Comprehensive testing and user documentation

---

## Testing Strategy

### Unit Tests

**Profile Service Tests:**
- Create profile (new, duplicate, overwrite)
- Update profile (valid, invalid)
- Delete profile (unused, in-use, force)
- Validate profile (all rules)
- Get templates (all, by category)

### Integration Tests

**API Tests:**
- All REST endpoints
- Error scenarios (404, 400, 409)
- Profile change workflow end-to-end

### Manual Testing

**UI Tests:**
- Create profile from template
- Edit existing profile
- Delete profile
- Change device profile at runtime
- Import/export profiles
- Search and filter profiles

---

## Success Criteria

**Phase 4.10 Complete When:**
- ✅ Profile Browser page functional
- ✅ Profile Editor supports create/edit/delete
- ✅ Runtime profile switching works (dynamic devices)
- ✅ Profile templates available
- ✅ Validation prevents invalid profiles
- ✅ Import/export working
- ✅ All tests pass
- ✅ User documentation complete

---

## Risk Assessment

**Risks:**
- UI complexity (many components)
- Validation rules comprehensive
- Profile change while device active
- Data loss during edit
- Performance with many profiles (50+)

**Mitigation:**
- Incremental development (task by task)
- Comprehensive validation testing
- Require device disconnect before profile change
- Auto-save/backup before changes
- Pagination/virtualization for large lists

---

## Timeline and Milestones

**Total Estimated Effort:** 11-13 hours (updated to include remote client API)

| Task | Duration | Cumulative |
|------|----------|-----------|
| Task 1: Profile Service | 2-3h | 3h |
| Task 2: REST API (Web UI + Remote Client) | 3-4h | 7h |
| Task 3: Browser Page | 3-4h | 11h |
| Task 4: Editor Page | 4-5h | 16h |
| Task 5: Runtime Switch | 2-3h | 19h |
| Task 6: Templates | 2-3h | 22h |
| Task 7: Testing | 2h | 24h |

**Milestones:**
1. Backend Complete (Tasks 1-2) - 7h (includes remote client API controller)
2. Browser Complete (Task 3) - 11h
3. Editor Complete (Task 4) - 16h
4. Full UI Complete (Tasks 5-6) - 22h
5. Phase 4.10 Complete (Task 7) - 24h

**Note:** Task 2 increased from 2-3h to 3-4h to account for new `ProfileApiController` with 8 remote client API endpoints, security validation, and bulk operations support.

---

## Future Enhancements

**Phase 4.11+:**
- Profile versioning/history
- Profile migration wizard
- Profile validation on import
- Profile sharing marketplace
- Advanced profile scripting
- Profile inheritance (base + overrides)
- Profile comparison tool
- Profile recommendations based on hardware

---

## References

- Phase 4.9 Implementation Plan: `docs/Phase-4.9-Profile-Loading-Implementation-Plan.md`
- Phase 4.9 Completion Status: `docs/Phase-4.9-Completion-Status.md`
- Phase 4.9 Architecture Analysis: `docs/Phase-4.9-Architecture-Analysis.md`

---

**Document History**

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-01-XX | AI Assistant | Initial Phase 4.10 planning document |
| 1.1 | 2025-01-XX | AI Assistant | Added Feature 7: Remote Client API Support with dedicated `/api/profiles` endpoints, server-side validation, bulk operations, and security controls. Updated Task 2 (+1h) and total effort (11-13h). |

---

**Phase 4.10 Status:** 📋 **PLANNING COMPLETE** - Ready for implementation
