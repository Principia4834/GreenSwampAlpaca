# Phase 4.10: Profile Management UI - Testing Guide

**Date:** 2025-01-XX  
**Version:** 1.0  
**Status:** 🧪 Ready for Testing  

---

## Overview

This document provides a comprehensive testing guide for Phase 4.10: Profile Management UI with Remote Client API support.

### What Was Implemented

**Backend (Tasks 1-2):**
- ✅ 4 new `ISettingsProfileService` interface methods
- ✅ 6 service implementation methods + 2 helpers
- ✅ 10 REST API endpoints (6 remote client + 4 web UI)
- ✅ `ProfileApiController` - Remote client API (370 lines)
- ✅ Extended `SetupDevicesController` - Web UI API (4 endpoints)

**Frontend (Tasks 3-6):**
- ✅ `ProfileBrowser.razor` - Main profile listing page (334 lines)
- ✅ `ProfileEditor.razor` - Create/edit profile form (402 lines)
- ✅ `ProfileSwitcher.razor` - Runtime profile switching modal (279 lines)
- ✅ `ProfileTemplates.razor` - Template selection page (231 lines)

**Integration:**
- ✅ Navigation link: "Settings Profiles" in main menu → `/profiles`
- ✅ Build successful - all components compile

---

## Testing Strategy

### Test Phases

| Phase | Focus | Duration | Prerequisites |
|-------|-------|----------|---------------|
| **1. Unit Testing** | Service methods, validators | 30 min | Code review |
| **2. API Testing** | REST endpoints | 45 min | Postman/Swagger |
| **3. UI Testing** | Blazor pages/components | 45 min | Browser |
| **4. Integration Testing** | End-to-end workflows | 30 min | All above pass |
| **5. Security Testing** | Validation, sanitization | 20 min | Security tools |
| **6. Regression Testing** | Existing features | 20 min | Full app test |

**Total Estimated Time:** ~3 hours

---

## 1. Unit Testing (Code Review & Manual Testing)

### Service Layer Tests

#### Test: `GetProfileDetailsAsync`
**Location:** `SettingsProfileService.cs` line ~678

```csharp
// Test Case 1: Get existing profile
var result = await profileService.GetProfileDetailsAsync("simulator-altaz");
Assert.NotNull(result);
Assert.Equal("simulator-altaz", result.Name);
Assert.NotNull(result.Settings);

// Test Case 2: Get non-existent profile
var result = await profileService.GetProfileDetailsAsync("nonexistent");
Assert.Null(result);
```

**Expected Results:**
- ✅ Returns full `SettingsProfile` with settings
- ✅ Returns `null` for non-existent profiles
- ✅ No exceptions thrown

---

#### Test: `SaveProfileSettingsAsync`
**Location:** `SettingsProfileService.cs` line ~689

```csharp
// Test Case 1: Create new profile (overwrite=false)
var settings = new SkySettings { /* ... */ };
var result = await profileService.SaveProfileSettingsAsync("test-profile", settings, overwrite: false);
Assert.True(result);

// Test Case 2: Update existing profile (overwrite=true)
var result = await profileService.SaveProfileSettingsAsync("test-profile", settings, overwrite: true);
Assert.True(result);

// Test Case 3: Attempt to overwrite without flag
var result = await profileService.SaveProfileSettingsAsync("test-profile", settings, overwrite: false);
Assert.False(result);
```

**Expected Results:**
- ✅ Creates new profile when doesn't exist
- ✅ Updates profile when overwrite=true
- ✅ Returns false when profile exists and overwrite=false
- ✅ Settings property correctly populated

---

#### Test: `IsProfileInUseAsync`
**Location:** `SettingsProfileService.cs` line ~755

```csharp
// Test Case 1: Profile not in use
var result = await profileService.IsProfileInUseAsync("simulator-altaz");
Assert.False(result); // Currently returns false (stub implementation)

// Test Case 2: Profile in use (after Phase 5.x integration)
// Connect a device with profile, then check
// var result = await profileService.IsProfileInUseAsync("active-profile");
// Assert.True(result);
```

**Expected Results (Current Phase 4.10):**
- ✅ Returns `false` for all profiles (stub implementation)
- ⚠️ TODO: Full implementation requires Phase 5.x instance tracking

---

#### Test: `GetDevicesUsingProfileAsync`
**Location:** `SettingsProfileService.cs` line ~766

```csharp
// Test Case 1: No devices using profile (current implementation)
var devices = await profileService.GetDevicesUsingProfileAsync("simulator-altaz");
Assert.Empty(devices);

// Test Case 2: After Phase 5.x - devices using profile
// Connect devices, then check
// var devices = await profileService.GetDevicesUsingProfileAsync("active-profile");
// Assert.Contains("Telescope/0", devices);
```

**Expected Results (Current Phase 4.10):**
- ✅ Returns empty list (stub implementation)
- ⚠️ TODO: Full implementation requires Phase 5.x device registry integration

---

### Helper Method Tests

#### Test: `CopySettingsToProfile`
**Location:** `SettingsProfileService.cs` line ~779

**Manual Test:**
1. Load existing profile
2. Modify settings object
3. Call `CopySettingsToProfile`
4. Verify profile updated

**Expected Results:**
- ✅ Settings object replaced
- ✅ Name, DisplayName, Description preserved
- ✅ CreatedDate preserved
- ✅ LastModified updated

---

#### Test: `CreateProfileFromSettings`
**Location:** `SettingsProfileService.cs` line ~807

**Manual Test:**
1. Create SkySettings object with AlignmentMode="GermanPolar"
2. Call `CreateProfileFromSettings("test", settings, "Test Profile")`
3. Verify profile created

**Expected Results:**
- ✅ Profile created with correct name
- ✅ AlignmentMode enum parsed correctly
- ✅ Settings object assigned
- ✅ Timestamps set

---

## 2. API Testing (REST Endpoints)

### Setup: Enable Swagger (Recommended)

**Add Swagger to Program.cs** (if not already present):

```csharp
// In Program.cs, add before builder.Build():
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// After app.Build(), before app.Run():
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

**Access Swagger UI:** `https://localhost:5000/swagger` (adjust port as needed)

---

### Remote Client API Endpoints (`/api/profiles`)

#### Endpoint 1: List All Profiles
**Route:** `GET /api/profiles`  
**Controller:** `ProfileApiController.ListProfiles`

**Test Cases:**

```http
### Test 1: Get all profiles
GET https://localhost:5000/api/profiles
Accept: application/json

### Expected Response: 200 OK
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
    }
  ],
  "count": 1,
  "serverVersion": "4.10.0"
}
```

**Verification Steps:**
1. ✅ Response status: 200 OK
2. ✅ `profiles` array contains all existing profiles
3. ✅ `count` matches array length
4. ✅ `serverVersion` present
5. ✅ Each profile has all metadata fields
6. ✅ `inUse` is `false` (stub implementation)

---

#### Endpoint 2: Download Single Profile
**Route:** `GET /api/profiles/{name}`  
**Controller:** `ProfileApiController.DownloadProfile`

**Test Cases:**

```http
### Test 1: Download existing profile
GET https://localhost:5000/api/profiles/simulator-altaz
Accept: application/json

### Expected Response: 200 OK
{
  "name": "simulator-altaz",
  "displayName": "AltAz Simulator",
  "description": "Simulator in AltAz mode",
  "settings": {
    "Mount": "Simulator",
    "AlignmentMode": "AltAz",
    "Latitude": 51.21135,
    // ... full settings object
  },
  "lastModified": "2025-01-15T10:30:00Z"
}
```

```http
### Test 2: Download non-existent profile
GET https://localhost:5000/api/profiles/nonexistent-profile
Accept: application/json

### Expected Response: 404 Not Found
{
  "error": "Profile 'nonexistent-profile' not found"
}
```

**Verification Steps:**
1. ✅ Existing profile returns 200 OK
2. ✅ Full settings object included
3. ✅ Non-existent profile returns 404 Not Found
4. ✅ Error message descriptive

---

#### Endpoint 3: Check Profile Exists (HEAD)
**Route:** `HEAD /api/profiles/{name}`  
**Controller:** `ProfileApiController.CheckProfileExists`

**Test Cases:**

```http
### Test 1: Check existing profile
HEAD https://localhost:5000/api/profiles/simulator-altaz

### Expected Response: 200 OK (no body)
```

```http
### Test 2: Check non-existent profile
HEAD https://localhost:5000/api/profiles/nonexistent

### Expected Response: 404 Not Found (no body)
```

**Verification Steps:**
1. ✅ Existing profile returns 200 OK
2. ✅ Non-existent returns 404 Not Found
3. ✅ No response body (HEAD request)
4. ✅ Faster than GET (no profile content transfer)

---

#### Endpoint 4: Get Profile Metadata Only
**Route:** `GET /api/profiles/{name}/metadata`  
**Controller:** `ProfileApiController.GetProfileMetadata`

**Test Cases:**

```http
### Test 1: Get metadata for existing profile
GET https://localhost:5000/api/profiles/simulator-altaz/metadata
Accept: application/json

### Expected Response: 200 OK
{
  "name": "simulator-altaz",
  "displayName": "AltAz Simulator",
  "mountType": "Simulator",
  "alignmentMode": "AltAz",
  "lastModified": "2025-01-15T10:30:00Z",
  "sizeBytes": 4256,
  "inUse": false,
  "devices": []
}
```

```http
### Test 2: Get metadata for non-existent profile
GET https://localhost:5000/api/profiles/nonexistent/metadata
Accept: application/json

### Expected Response: 404 Not Found
```

**Verification Steps:**
1. ✅ Returns metadata without full settings (efficient)
2. ✅ Includes `sizeBytes` estimate
3. ✅ Non-existent profile returns 404 Not Found
4. ✅ Response size < 1KB (metadata only)

---

#### Endpoint 5: Upload/Update Profile
**Route:** `PUT /api/profiles/{name}`  
**Controller:** `ProfileApiController.UploadProfile`

**Test Cases:**

```http
### Test 1: Upload new profile (create)
PUT https://localhost:5000/api/profiles/test-custom-profile
Content-Type: application/json

{
  "profile": {
    "Mount": "SkyWatcher",
    "AlignmentMode": "GermanPolar",
    "Latitude": 51.21135,
    "Longitude": -1.49045,
    "Elevation": 100.0,
    "SiteName": "Test Observatory",
    "ApertureArea": 0.049087,
    "ApertureDiameter": 0.25,
    "FocalLength": 1.5,
    "Handshake": "None",
    "BaudRate": 115200,
    "DTREnable": false,
    "DataBits": 8,
    "Parity": "None",
    "StopBits": "One",
    "ComPort": "COM3",
    "AutoTrack": true,
    "TrackingRate": "Sidereal",
    "GuideRateRightAscensionArcsecPerSec": 15.0,
    "GuideRateDeclinationArcsecPerSec": 15.0
  },
  "options": {
    "overwrite": false,
    "validate": true
  }
}

### Expected Response: 200 OK
{
  "success": true,
  "profileName": "test-custom-profile",
  "action": "created",
  "message": "Profile 'test-custom-profile' created successfully"
}
```

```http
### Test 2: Update existing profile (overwrite)
PUT https://localhost:5000/api/profiles/test-custom-profile
Content-Type: application/json

{
  "profile": {
    "Mount": "SkyWatcher",
    "AlignmentMode": "GermanPolar",
    "Latitude": 52.0,  // Changed
    // ... other fields
  },
  "options": {
    "overwrite": true,
    "validate": true
  }
}

### Expected Response: 200 OK
{
  "success": true,
  "profileName": "test-custom-profile",
  "action": "updated",
  "message": "Profile 'test-custom-profile' updated successfully"
}
```

```http
### Test 3: Attempt to overwrite without flag
PUT https://localhost:5000/api/profiles/test-custom-profile
Content-Type: application/json

{
  "profile": { /* ... */ },
  "options": {
    "overwrite": false,
    "validate": true
  }
}

### Expected Response: 409 Conflict
{
  "success": false,
  "profileName": "test-custom-profile",
  "action": null,
  "message": "Profile 'test-custom-profile' already exists. Set overwrite to true to update."
}
```

```http
### Test 4: Upload invalid profile (validation failure)
PUT https://localhost:5000/api/profiles/test-invalid-profile
Content-Type: application/json

{
  "profile": {
    "Mount": "SkyWatcher",
    "AlignmentMode": "GermanPolar",
    "Latitude": 95.5,  // INVALID: > 90
    "Longitude": 200.0,  // INVALID: > 180
    // ... other fields
  },
  "options": {
    "overwrite": false,
    "validate": true
  }
}

### Expected Response: 400 Bad Request
{
  "isValid": false,
  "errors": [
    {
      "field": "Latitude",
      "message": "Latitude must be between -90 and 90 degrees",
      "value": "95.5"
    },
    {
      "field": "Longitude",
      "message": "Longitude must be between -180 and 180 degrees",
      "value": "200.0"
    }
  ],
  "warnings": []
}
```

**Verification Steps:**
1. ✅ Create new profile returns 200 OK with "created" action
2. ✅ Update existing profile (overwrite=true) returns 200 OK with "updated" action
3. ✅ Duplicate without overwrite returns 409 Conflict
4. ✅ Invalid profile returns 400 Bad Request with validation errors
5. ✅ Profile name sanitization prevents path traversal (see security tests)
6. ✅ Server-side validation enforced (cannot bypass)

---

#### Endpoint 6: Validate Profile (Without Saving)
**Route:** `POST /api/profiles/{name}/validate`  
**Controller:** `ProfileApiController.ValidateProfile`

**Test Cases:**

```http
### Test 1: Validate valid profile
POST https://localhost:5000/api/profiles/test-validation/validate
Content-Type: application/json

{
  "Mount": "SkyWatcher",
  "AlignmentMode": "GermanPolar",
  "Latitude": 51.21135,
  "Longitude": -1.49045,
  // ... other valid fields
}

### Expected Response: 200 OK
{
  "isValid": true,
  "errors": [],
  "warnings": []
}
```

```http
### Test 2: Validate invalid profile
POST https://localhost:5000/api/profiles/test-validation/validate
Content-Type: application/json

{
  "Mount": "SkyWatcher",
  "AlignmentMode": "GermanPolar",
  "Latitude": -120.0,  // INVALID
  "Longitude": -1.49045,
  "Elevation": -50.0,  // INVALID (negative elevation unusual)
  // ... other fields
}

### Expected Response: 200 OK (validation result)
{
  "isValid": false,
  "errors": [
    {
      "field": "Latitude",
      "message": "Latitude must be between -90 and 90 degrees",
      "value": "-120.0"
    }
  ],
  "warnings": [
    {
      "field": "Elevation",
      "message": "Elevation is negative. This is unusual.",
      "value": "-50.0"
    }
  ]
}
```

**Verification Steps:**
1. ✅ Valid profile returns `isValid: true`
2. ✅ Invalid profile returns `isValid: false` with field-level errors
3. ✅ Profile NOT saved (validation only)
4. ✅ Warnings included for unusual values
5. ✅ Same validation logic as upload endpoint

---

### Web UI API Endpoints (`/setup/profiles`)

#### Endpoint 7: Get Profile
**Route:** `GET /setup/profiles/{name}`  
**Controller:** `SetupDevicesController.GetProfile`

**Test Cases:**

```http
### Test 1: Get existing profile
GET https://localhost:5000/setup/profiles/simulator-altaz
Accept: application/json

### Expected Response: 200 OK (SettingsProfile object)
{
  "name": "simulator-altaz",
  "displayName": "AltAz Simulator",
  "description": "Simulator in AltAz mode",
  "alignmentMode": "AltAz",
  "settings": {
    "Mount": "Simulator",
    "AlignmentMode": "AltAz",
    // ... full settings
  },
  "createdDate": "2025-01-01T00:00:00Z",
  "lastModified": "2025-01-15T10:30:00Z"
}
```

**Verification Steps:**
1. ✅ Returns full `SettingsProfile` object
2. ✅ Includes metadata and settings
3. ✅ 404 Not Found for non-existent profiles

---

#### Endpoint 8: Create Profile
**Route:** `POST /setup/profiles`  
**Controller:** `SetupDevicesController.CreateProfile`

**Test Cases:**

```http
### Test 1: Create new profile
POST https://localhost:5000/setup/profiles
Content-Type: application/json

{
  "name": "test-ui-profile",
  "displayName": "Test UI Profile",
  "description": "Created via web UI endpoint",
  "alignmentMode": "GermanPolar",
  "settings": {
    "Mount": "SkyWatcher",
    "AlignmentMode": "GermanPolar",
    "Latitude": 51.21135,
    // ... other fields
  }
}

### Expected Response: 200 OK
{
  "name": "test-ui-profile",
  "displayName": "Test UI Profile",
  // ... full profile
}
```

**Verification Steps:**
1. ✅ Profile created successfully
2. ✅ Returns created profile object
3. ✅ Profile persisted to disk
4. ✅ 400 Bad Request if profile already exists

---

#### Endpoint 9: Update Profile
**Route:** `PUT /setup/profiles/{name}`  
**Controller:** `SetupDevicesController.UpdateProfile`

**Test Cases:**

```http
### Test 1: Update existing profile
PUT https://localhost:5000/setup/profiles/test-ui-profile
Content-Type: application/json

{
  "name": "test-ui-profile",
  "displayName": "Updated Test Profile",
  "description": "Updated description",
  "alignmentMode": "GermanPolar",
  "settings": {
    "Mount": "SkyWatcher",
    "Latitude": 52.0,  // Changed
    // ... other fields
  }
}

### Expected Response: 200 OK
```

**Verification Steps:**
1. ✅ Profile updated successfully
2. ✅ LastModified timestamp updated
3. ✅ Validation enforced before save
4. ✅ 404 Not Found if profile doesn't exist

---

#### Endpoint 10: Delete Profile
**Route:** `DELETE /setup/profiles/{name}?force={bool}`  
**Controller:** `SetupDevicesController.DeleteProfile`

**Test Cases:**

```http
### Test 1: Delete unused profile (no force flag)
DELETE https://localhost:5000/setup/profiles/test-ui-profile

### Expected Response: 204 No Content
```

```http
### Test 2: Delete profile in use without force flag
DELETE https://localhost:5000/setup/profiles/active-profile

### Expected Response: 400 Bad Request
{
  "message": "Profile 'active-profile' is currently in use by device(s). Use force=true to delete anyway.",
  "devicesUsingProfile": ["Telescope/0"]
}
```

```http
### Test 3: Force delete profile in use
DELETE https://localhost:5000/setup/profiles/active-profile?force=true

### Expected Response: 204 No Content
```

**Verification Steps:**
1. ✅ Unused profile deleted without force flag
2. ✅ Profile in use requires `force=true` (Phase 5.x - currently allows delete)
3. ✅ Profile file removed from disk
4. ✅ 404 Not Found if profile doesn't exist

---

## 3. UI Testing (Blazor Pages)

### Setup
1. Start application: `dotnet run` (or F5 in Visual Studio)
2. Open browser: `https://localhost:5000` (adjust port)
3. Navigate to: **Settings Profiles** in left menu

---

### Page 1: Profile Browser (`/profiles`)

#### Test 1: Initial Load
**Steps:**
1. Click "Settings Profiles" in navigation menu
2. Observe profile list

**Expected Results:**
- ✅ Page loads without errors
- ✅ All existing profiles displayed as cards
- ✅ Each card shows: name, display name, alignment mode, mount type
- ✅ Action buttons visible: View, Edit, Duplicate, Export, Delete

---

#### Test 2: Search Functionality
**Steps:**
1. Type "simulator" in search box
2. Observe filtered results

**Expected Results:**
- ✅ Only profiles matching "simulator" displayed
- ✅ Search is case-insensitive
- ✅ Searches name, display name, and description
- ✅ Results update immediately

---

#### Test 3: Filter by Alignment Mode
**Steps:**
1. Select "AltAz" from alignment mode filter
2. Observe filtered results
3. Select "German Polar" from alignment mode filter
4. Observe filtered results

**Expected Results:**
- ✅ Only AltAz profiles shown when filter selected
- ✅ Only German Polar profiles shown when filter selected
- ✅ "All Alignment Modes" shows all profiles
- ✅ Filter combines with search

---

#### Test 4: Filter by In-Use Status
**Steps:**
1. Select "In Use Only" from status filter
2. Observe filtered results

**Expected Results:**
- ✅ Only profiles marked as "in use" displayed
- ⚠️ Currently all profiles show "Not in use" (stub implementation)
- ⚠️ Phase 5.x will enable real-time tracking

---

#### Test 5: View Profile
**Steps:**
1. Click "View" button on a profile card
2. Observe behavior

**Expected Results:**
- ✅ Navigates to profile editor in read-only mode (or details page)
- ✅ Profile data displayed
- ⚠️ TODO: Consider adding read-only details view

---

#### Test 6: Edit Profile
**Steps:**
1. Click "Edit" button on a profile card
2. Observe navigation

**Expected Results:**
- ✅ Navigates to `/profiles/{name}/edit`
- ✅ Profile editor loads with profile data
- ✅ Edit mode enabled

---

#### Test 7: Duplicate Profile
**Steps:**
1. Click "Duplicate" button on "simulator-altaz" profile
2. Observe behavior

**Expected Results:**
- ✅ Navigates to profile editor in create mode
- ✅ Profile data pre-filled from source profile
- ✅ Profile name changed to "simulator-altaz-copy" (or similar)
- ✅ Save creates new profile (doesn't overwrite original)

---

#### Test 8: Export Profile
**Steps:**
1. Click "Export" button on a profile card
2. Observe download

**Expected Results:**
- ✅ Browser downloads JSON file
- ✅ Filename: `{profile-name}.json`
- ✅ File contains valid JSON
- ✅ File includes full profile settings

---

#### Test 9: Delete Profile (Cancel)
**Steps:**
1. Click "Delete" button on a profile card
2. Observe confirmation modal
3. Click "Cancel"

**Expected Results:**
- ✅ Confirmation modal appears
- ✅ Modal shows profile name
- ✅ Warning about action being permanent
- ✅ "Cancel" button closes modal
- ✅ Profile NOT deleted

---

#### Test 10: Delete Profile (Confirm)
**Steps:**
1. Click "Delete" button on "test-ui-profile"
2. Click "Delete" in confirmation modal
3. Observe result

**Expected Results:**
- ✅ Profile deleted from server
- ✅ Profile card removed from grid
- ✅ Success message displayed (toast/alert)
- ✅ No page reload (SPA behavior)

---

#### Test 11: Create New Profile Button
**Steps:**
1. Click "Create New Profile" button
2. Observe navigation

**Expected Results:**
- ✅ Navigates to `/profiles/new`
- ✅ Profile editor loads in create mode
- ✅ All fields empty/default values

---

#### Test 12: Create from Template Button
**Steps:**
1. Click "Create from Template" button
2. Observe navigation

**Expected Results:**
- ✅ Navigates to `/profiles/new/template`
- ✅ Template selection page loads
- ✅ 3 template cards displayed

---

### Page 2: Profile Editor (`/profiles/new`, `/profiles/{name}/edit`)

#### Test 13: Create New Profile (Basic)
**Steps:**
1. Navigate to `/profiles/new`
2. Enter profile name: "test-manual-profile"
3. Enter display name: "Test Manual Profile"
4. Enter description: "Created manually"
5. Select alignment mode: "German Polar"
6. Select mount type: "SkyWatcher"
7. Click "Save Profile"

**Expected Results:**
- ✅ All form fields visible and editable
- ✅ Profile created successfully
- ✅ Success message displayed
- ✅ Redirects to profile browser
- ✅ New profile appears in list

---

#### Test 14: Edit Existing Profile
**Steps:**
1. Navigate to `/profiles/simulator-altaz/edit`
2. Observe pre-filled data
3. Change display name to "Updated Simulator"
4. Change latitude to 52.0
5. Click "Save Changes"

**Expected Results:**
- ✅ Profile data pre-filled correctly
- ✅ All fields editable
- ✅ Changes saved successfully
- ✅ LastModified timestamp updated
- ✅ Success message displayed

---

#### Test 15: Validation - Required Fields
**Steps:**
1. Navigate to `/profiles/new`
2. Leave profile name empty
3. Click "Save Profile"

**Expected Results:**
- ✅ Validation error displayed
- ✅ "Profile name is required" message
- ✅ Form not submitted
- ✅ Error styling on field (red border, etc.)

---

#### Test 16: Validation - Latitude Range
**Steps:**
1. Navigate to `/profiles/new`
2. Enter profile name: "test-validation"
3. Enter latitude: 95.5 (invalid)
4. Click "Validate Only"

**Expected Results:**
- ✅ Validation triggered
- ✅ Error message: "Latitude must be between -90 and 90"
- ✅ Profile NOT saved
- ✅ User can correct and re-validate

---

#### Test 17: Validation - Longitude Range
**Steps:**
1. Enter longitude: 200.0 (invalid)
2. Click "Validate Only"

**Expected Results:**
- ✅ Error message: "Longitude must be between -180 and 180"
- ✅ Field-level error displayed

---

#### Test 18: Conditional Hardware Section
**Steps:**
1. Create new profile
2. Select mount type: "Simulator"
3. Observe hardware section
4. Select mount type: "SkyWatcher"
5. Observe hardware section

**Expected Results:**
- ✅ Hardware section HIDDEN for Simulator
- ✅ Hardware section VISIBLE for SkyWatcher
- ✅ Port, baud rate, etc. fields appear
- ✅ No console errors

---

#### Test 19: Cancel Button
**Steps:**
1. Navigate to `/profiles/new`
2. Enter some data
3. Click "Cancel"

**Expected Results:**
- ✅ Navigates back to `/profiles`
- ✅ Data NOT saved
- ✅ No errors

---

#### Test 20: Section Collapsing (if implemented)
**Steps:**
1. Observe 6 sections: Basic Info, Location, Hardware, Tracking, Guiding, Optics
2. Click section headers (if collapsible)

**Expected Results:**
- ✅ All sections visible by default
- ✅ Sections collapsible (optional feature)
- ✅ Smooth UI transitions

---

### Page 3: Profile Templates (`/profiles/new/template`)

#### Test 21: Template Cards Display
**Steps:**
1. Navigate to `/profiles/new/template`
2. Observe template cards

**Expected Results:**
- ✅ 3 template cards displayed: AltAz, Polar, German Polar
- ✅ Each card shows: alignment mode, features, use cases
- ✅ Cards have hover effects
- ✅ Professional styling

---

#### Test 22: Select AltAz Template
**Steps:**
1. Click "Select" on AltAz template card
2. Observe form

**Expected Results:**
- ✅ Template selected (card highlighted)
- ✅ Profile name suggestion appears: "altaz-profile"
- ✅ Display name suggestion: "AltAz Profile"
- ✅ Form fields enabled

---

#### Test 23: Create Profile from Template
**Steps:**
1. Select "German Polar" template
2. Enter profile name: "my-german-polar"
3. Enter display name: "My German Polar Profile"
4. Enter description: "Created from template"
5. Click "Create Profile"

**Expected Results:**
- ✅ Profile created with template settings
- ✅ Navigates to profile editor (`/profiles/my-german-polar/edit`)
- ✅ Settings pre-filled from template
- ✅ User can customize settings
- ✅ Save creates final profile

---

#### Test 24: Template Validation
**Steps:**
1. Select a template
2. Leave profile name empty
3. Click "Create Profile"

**Expected Results:**
- ✅ Validation error: "Profile name is required"
- ✅ Form not submitted
- ✅ User can correct and retry

---

#### Test 25: Cancel Template Selection
**Steps:**
1. Select a template
2. Click "Back" or "Cancel"

**Expected Results:**
- ✅ Navigates back to `/profiles`
- ✅ No profile created
- ✅ No errors

---

### Component: Profile Switcher Modal (`ProfileSwitcher.razor`)

**Note:** This component requires integration into device pages. For Phase 4.10, test in isolation or integrate into telescope setup page.

#### Test 26: Open Profile Switcher
**Steps:**
1. Navigate to telescope setup page (or page with switcher integrated)
2. Click "Change Profile" button
3. Observe modal

**Expected Results:**
- ✅ Modal opens
- ✅ Current profile displayed
- ✅ List of available profiles shown
- ✅ Reserved slot warning (if device is slot 0 or 1)
- ✅ Connected device warning (if device connected)

---

#### Test 27: Select Profile in Switcher
**Steps:**
1. Open profile switcher
2. Click on a profile in the list
3. Observe preview panel

**Expected Results:**
- ✅ Profile selected (highlighted)
- ✅ Preview panel shows profile details
- ✅ Alignment mode, mount type, location displayed
- ✅ "Apply" button enabled

---

#### Test 28: Apply Profile Change (Current Limitation)
**Steps:**
1. Select a different profile
2. Click "Apply Profile"
3. Observe result

**Expected Results:**
- ⚠️ **Known Limitation:** Full mount reload requires restart
- ✅ Profile reference updated
- ✅ Message: "Profile updated. Restart server to apply changes." (or similar)
- ✅ Modal closes
- ⚠️ TODO: Phase 5.x will enable hot-reload

**Why Limitation Exists:**
- `SkySettings` (JSON model) vs `SkySettingsInstance` (runtime) type mismatch
- Phase 4.9 architecture uses static facade
- Phase 5.x will implement instance-based architecture

---

#### Test 29: Cancel Profile Change
**Steps:**
1. Open profile switcher
2. Select a different profile
3. Click "Cancel"

**Expected Results:**
- ✅ Modal closes
- ✅ Profile NOT changed
- ✅ No errors

---

#### Test 30: Reserved Slot Warning
**Steps:**
1. Open profile switcher for device slot 0 or 1
2. Observe warning message

**Expected Results:**
- ✅ Warning: "This is a reserved slot. Changing the profile may affect ASCOM compatibility."
- ✅ User can still proceed
- ✅ Warning styled prominently

---

## 4. Integration Testing (End-to-End Workflows)

### Workflow 1: Create Profile from Template → Edit → Use

**Steps:**
1. Navigate to Settings Profiles
2. Click "Create from Template"
3. Select "German Polar" template
4. Enter name: "my-custom-polar"
5. Click "Create Profile"
6. In profile editor, customize latitude/longitude
7. Click "Save Changes"
8. Return to profile browser
9. Verify profile appears in list
10. Navigate to telescope device setup
11. Open profile switcher
12. Select "my-custom-polar" profile
13. Apply profile change

**Expected Results:**
- ✅ Profile created from template
- ✅ Profile customized in editor
- ✅ Profile appears in browser
- ✅ Profile selectable in switcher
- ✅ Profile applied to device (with restart requirement)

---

### Workflow 2: Duplicate → Modify → Export → Import

**Steps:**
1. Navigate to Settings Profiles
2. Click "Duplicate" on "simulator-altaz"
3. Profile editor opens with "simulator-altaz-copy"
4. Change alignment mode to "Polar"
5. Save profile
6. Return to browser
7. Click "Export" on "simulator-altaz-copy"
8. Download JSON file
9. Delete "simulator-altaz-copy" profile
10. (Future: Import JSON file to restore)

**Expected Results:**
- ✅ Profile duplicated successfully
- ✅ Modifications saved
- ✅ Export downloads valid JSON
- ✅ Profile deleted
- ⚠️ Import feature not yet implemented (future phase)

---

### Workflow 3: Create → Validate → Fix Errors → Save

**Steps:**
1. Navigate to `/profiles/new`
2. Enter profile name: "validation-test"
3. Enter invalid latitude: 120.0
4. Enter invalid longitude: 200.0
5. Click "Validate Only"
6. Observe validation errors
7. Correct latitude to 51.21135
8. Correct longitude to -1.49045
9. Click "Validate Only" again
10. Observe success
11. Click "Save Profile"

**Expected Results:**
- ✅ Validation detects errors
- ✅ Field-level errors displayed
- ✅ Errors highlighted in red
- ✅ After correction, validation passes
- ✅ Profile saved successfully

---

### Workflow 4: Search → Filter → Edit → Verify

**Steps:**
1. Navigate to Settings Profiles
2. Enter search term: "simulator"
3. Observe filtered results
4. Select "German Polar" from alignment mode filter
5. Observe combined filter (no results expected if no German Polar simulators)
6. Clear filters
7. Click "Edit" on "simulator-altaz"
8. Change display name to "AltAz Simulator Updated"
9. Save changes
10. Return to browser
11. Verify updated display name shown

**Expected Results:**
- ✅ Search filters profiles
- ✅ Alignment mode filter works
- ✅ Combined filters work
- ✅ Edit modifies profile
- ✅ Changes reflected in browser
- ✅ No page reload (SPA behavior)

---

## 5. Security Testing

### Test 31: Profile Name Sanitization (Path Traversal)

**API Test:**

```http
### Attempt path traversal in profile name
PUT https://localhost:5000/api/profiles/../../../etc/passwd
Content-Type: application/json

{
  "profile": { /* ... */ },
  "options": { "overwrite": false, "validate": true }
}

### Expected Response: 400 Bad Request
{
  "success": false,
  "message": "Invalid profile name. Only alphanumeric characters, hyphens, and underscores are allowed."
}
```

**Verification:**
- ✅ Profile name validated with regex: `^[a-zA-Z0-9_-]+$`
- ✅ Path traversal attempts rejected
- ✅ No files created outside profiles directory

---

### Test 32: Profile Size Limit

**API Test:**

```http
### Upload very large profile (>1MB)
PUT https://localhost:5000/api/profiles/large-profile
Content-Type: application/json

{
  "profile": {
    "Mount": "SkyWatcher",
    "AlignmentMode": "GermanPolar",
    "Description": "A".repeat(1048576 * 2),  // 2MB string
    // ... other fields
  },
  "options": { "overwrite": false, "validate": true }
}

### Expected Response: 400 Bad Request
{
  "success": false,
  "message": "Profile size exceeds maximum allowed size of 1MB"
}
```

**Verification:**
- ✅ Profile size checked before processing
- ✅ Requests >1MB rejected
- ✅ DOS attack prevented

---

### Test 33: SQL Injection Attempts

**API Test:**

```http
### Attempt SQL injection in profile name
GET https://localhost:5000/api/profiles/'; DROP TABLE profiles;--

### Expected Response: 400 Bad Request (invalid name) or 404 Not Found
```

**Verification:**
- ✅ Profile name sanitization prevents SQL injection
- ✅ No database queries executed (file-based storage)
- ✅ No error messages revealing internal structure

---

### Test 34: XSS Attempts in Profile Data

**UI Test:**

**Steps:**
1. Create profile with name: `<script>alert('XSS')</script>`
2. Save profile
3. Navigate to profile browser
4. Observe profile card

**Expected Results:**
- ✅ Script NOT executed
- ✅ Profile name escaped/sanitized
- ✅ Display shows literal text, not code

---

### Test 35: Concurrent Upload Stress Test

**API Test:**

```bash
# Use tool like Apache Bench or k6
ab -n 100 -c 10 -T 'application/json' \
  -p upload.json \
  https://localhost:5000/api/profiles/concurrent-test
```

**Verification:**
- ✅ Server handles concurrent requests
- ✅ No race conditions in file writes
- ✅ No data corruption
- ✅ Appropriate error handling

---

### Test 36: Authentication (Future Phase)

**Current Status:**
- ⚠️ No authentication implemented in Phase 4.10
- ⚠️ All API endpoints publicly accessible
- ⚠️ Defer to Phase 4.11+ for API key or OAuth

**Future Test:**

```http
### Unauthenticated request should fail
PUT https://localhost:5000/api/profiles/test-auth
Content-Type: application/json
# No Authorization header

### Expected Response: 401 Unauthorized
```

---

## 6. Regression Testing

### Test 37: Existing Mount Settings Page

**Steps:**
1. Navigate to "Mount Settings" page
2. Verify page loads
3. Change a setting (e.g., AutoTrack)
4. Save changes
5. Verify settings persist

**Expected Results:**
- ✅ Mount settings page still functional
- ✅ No interference from profile management
- ✅ Settings save correctly
- ✅ No JavaScript errors

---

### Test 38: Device Manager

**Steps:**
1. Navigate to "Device Manager" page
2. Verify device list displays
3. Add/remove a device (if applicable)
4. Verify no errors

**Expected Results:**
- ✅ Device manager still functional
- ✅ No conflicts with profile API
- ✅ Device operations work

---

### Test 39: Telescope Control

**Steps:**
1. Connect to telescope device
2. Perform basic operations (slew, track, etc.)
3. Verify no errors

**Expected Results:**
- ✅ Telescope control unaffected
- ✅ No profile-related errors during operation
- ✅ Existing functionality preserved

---

### Test 40: ASCOM Alpaca API

**Steps:**
1. Use ASCOM client (e.g., Cartes du Ciel)
2. Connect to telescope via Alpaca
3. Perform operations
4. Verify ASCOM compatibility

**Expected Results:**
- ✅ ASCOM Alpaca API still functional
- ✅ No breaking changes
- ✅ Existing clients work

---

## Test Results Template

### Test Execution Checklist

Copy this checklist and mark tests as you complete them:

```markdown
## Phase 4.10 Test Execution - [Date]

### 1. Unit Testing (Service Layer)
- [ ] Test: GetProfileDetailsAsync (TC1, TC2)
- [ ] Test: SaveProfileSettingsAsync (TC1, TC2, TC3)
- [ ] Test: IsProfileInUseAsync (TC1)
- [ ] Test: GetDevicesUsingProfileAsync (TC1)
- [ ] Test: CopySettingsToProfile
- [ ] Test: CreateProfileFromSettings

### 2. API Testing (REST Endpoints)
#### Remote Client API (`/api/profiles`)
- [ ] Test 1: List All Profiles (GET /api/profiles)
- [ ] Test 2: Download Single Profile (GET /api/profiles/{name})
- [ ] Test 3: Check Profile Exists (HEAD /api/profiles/{name})
- [ ] Test 4: Get Profile Metadata (GET /api/profiles/{name}/metadata)
- [ ] Test 5: Upload Profile - Create (PUT /api/profiles/{name})
- [ ] Test 5: Upload Profile - Update (PUT /api/profiles/{name})
- [ ] Test 5: Upload Profile - Duplicate Conflict (PUT /api/profiles/{name})
- [ ] Test 5: Upload Profile - Validation Failure (PUT /api/profiles/{name})
- [ ] Test 6: Validate Profile (POST /api/profiles/{name}/validate)

#### Web UI API (`/setup/profiles`)
- [ ] Test 7: Get Profile (GET /setup/profiles/{name})
- [ ] Test 8: Create Profile (POST /setup/profiles)
- [ ] Test 9: Update Profile (PUT /setup/profiles/{name})
- [ ] Test 10: Delete Profile (DELETE /setup/profiles/{name})

### 3. UI Testing (Blazor Pages)
#### Profile Browser (`/profiles`)
- [ ] Test 11: Initial Load
- [ ] Test 12: Search Functionality
- [ ] Test 13: Filter by Alignment Mode
- [ ] Test 14: Filter by In-Use Status
- [ ] Test 15: View Profile
- [ ] Test 16: Edit Profile
- [ ] Test 17: Duplicate Profile
- [ ] Test 18: Export Profile
- [ ] Test 19: Delete Profile (Cancel)
- [ ] Test 20: Delete Profile (Confirm)
- [ ] Test 21: Create New Profile Button
- [ ] Test 22: Create from Template Button

#### Profile Editor (`/profiles/new`, `/profiles/{name}/edit`)
- [ ] Test 23: Create New Profile (Basic)
- [ ] Test 24: Edit Existing Profile
- [ ] Test 25: Validation - Required Fields
- [ ] Test 26: Validation - Latitude Range
- [ ] Test 27: Validation - Longitude Range
- [ ] Test 28: Conditional Hardware Section
- [ ] Test 29: Cancel Button
- [ ] Test 30: Section Layout

#### Profile Templates (`/profiles/new/template`)
- [ ] Test 31: Template Cards Display
- [ ] Test 32: Select AltAz Template
- [ ] Test 33: Create Profile from Template
- [ ] Test 34: Template Validation
- [ ] Test 35: Cancel Template Selection

#### Profile Switcher Component
- [ ] Test 36: Open Profile Switcher
- [ ] Test 37: Select Profile in Switcher
- [ ] Test 38: Apply Profile Change (with limitation)
- [ ] Test 39: Cancel Profile Change
- [ ] Test 40: Reserved Slot Warning

### 4. Integration Testing (End-to-End)
- [ ] Workflow 1: Create from Template → Edit → Use
- [ ] Workflow 2: Duplicate → Modify → Export
- [ ] Workflow 3: Create → Validate → Fix → Save
- [ ] Workflow 4: Search → Filter → Edit → Verify

### 5. Security Testing
- [ ] Test 41: Profile Name Sanitization (Path Traversal)
- [ ] Test 42: Profile Size Limit
- [ ] Test 43: SQL Injection Attempts
- [ ] Test 44: XSS Attempts in Profile Data
- [ ] Test 45: Concurrent Upload Stress Test

### 6. Regression Testing
- [ ] Test 46: Existing Mount Settings Page
- [ ] Test 47: Device Manager
- [ ] Test 48: Telescope Control
- [ ] Test 49: ASCOM Alpaca API

---

## Issues Found

| Test # | Issue Description | Severity | Status |
|--------|-------------------|----------|--------|
| | | | |

---

## Test Summary

**Date:** [Date]  
**Tester:** [Name]  
**Environment:** [Dev/Staging/Production]  
**Browser:** [Browser + Version]  

**Results:**
- **Total Tests:** 49
- **Passed:** __
- **Failed:** __
- **Blocked:** __
- **Skipped:** __

**Overall Status:** ✅ PASS / ❌ FAIL / ⚠️ PARTIAL

**Notes:**
- 

**Sign-off:** _______________
```

---

## Known Limitations (Phase 4.10)

### 1. Runtime Profile Switching
**Status:** ⚠️ UI Infrastructure Complete, Full Implementation Deferred

**Details:**
- `ProfileSwitcher.razor` component complete (279 lines)
- Profile selection and preview working
- Profile reference can be updated
- **Limitation:** Full mount reload requires server restart
- **Reason:** SkySettings (JSON) vs SkySettingsInstance (runtime) type mismatch
- **Resolution:** Phase 5.x instance-based architecture

**Workaround:**
1. User selects new profile via ProfileSwitcher
2. Profile reference updated
3. Application displays message: "Profile updated. Restart server to apply changes."
4. User restarts server manually
5. New profile settings loaded on startup

---

### 2. Profile In-Use Detection
**Status:** ⚠️ Stub Implementation

**Details:**
- `IsProfileInUseAsync()` always returns `false`
- `GetDevicesUsingProfileAsync()` returns empty list
- **Reason:** Requires device registry integration (Phase 4.8 infrastructure exists)
- **Resolution:** Phase 5.x will connect ProfileService to MountInstanceRegistry

**Impact:**
- Profile browser shows all profiles as "Not in use"
- Delete operation doesn't warn about active usage
- User can delete active profile (may cause errors)

**Workaround:**
- User should verify profile not in use before deleting
- Check device setup pages for current profile

---

### 3. Bulk Profile Operations
**Status:** ⚠️ Not Implemented

**Details:**
- Download all profiles (ZIP) - not implemented
- Upload multiple profiles (batch) - not implemented
- **Reason:** Scope management for Phase 4.10
- **Resolution:** Phase 4.11+ if needed

**Workaround:**
- Export profiles individually
- Upload profiles individually

---

### 4. Profile Import (UI)
**Status:** ⚠️ Not Implemented

**Details:**
- Profile browser has "Export" button
- No "Import" button yet
- API supports upload (PUT endpoint)
- **Reason:** Deferred to keep Phase 4.10 focused
- **Resolution:** Phase 4.11+ if needed

**Workaround:**
- Use API endpoint to upload profiles
- Or manually copy JSON files to profiles directory

---

### 5. Authentication/Authorization
**Status:** ⚠️ Not Implemented

**Details:**
- All API endpoints publicly accessible
- No authentication required
- No authorization checks
- **Reason:** Deferred to future phase
- **Resolution:** Phase 4.11+ (API key or OAuth)

**Security Risk:**
- ⚠️ If server exposed to internet, anyone can upload/delete profiles
- **Mitigation:** Use firewall to restrict access, deploy behind VPN, or implement auth in Phase 4.11+

---

## Testing Tools Recommended

### API Testing
1. **Swagger/OpenAPI** (recommended)
   - Enable in `Program.cs`
   - Access: `https://localhost:5000/swagger`
   - Interactive API documentation
   - Test endpoints directly in browser

2. **Postman**
   - Create collection for all 10 endpoints
   - Save test cases
   - Automate regression tests

3. **curl** (command line)
   ```bash
   curl -X GET https://localhost:5000/api/profiles
   ```

### UI Testing
1. **Browser DevTools**
   - Console for JavaScript errors
   - Network tab for API calls
   - Application tab for storage

2. **Blazor DevTools** (in browser)
   - Component inspection
   - State debugging

3. **Visual Studio Debugger**
   - Breakpoints in Razor files
   - Step through code
   - Inspect variables

### Security Testing
1. **OWASP ZAP** (recommended)
   - Automated security scanning
   - Path traversal detection
   - XSS detection

2. **Burp Suite Community**
   - Manual security testing
   - Request interception
   - Payload testing

### Load Testing
1. **Apache Bench (ab)**
   - Simple HTTP load testing
   - Concurrent request testing

2. **k6** (recommended for modern APIs)
   - JavaScript-based load testing
   - Realistic scenarios

---

## Success Criteria

Phase 4.10 testing is complete when:

### Backend
- ✅ All 10 REST API endpoints operational
- ✅ Server-side validation enforced
- ✅ Security controls verified (name sanitization, size limits)
- ✅ Profile CRUD operations working
- ✅ No errors in API responses

### Frontend
- ✅ All 4 Blazor pages/components functional
- ✅ Profile browser displays profiles correctly
- ✅ Profile editor creates/updates profiles
- ✅ Profile templates create profiles from templates
- ✅ Profile switcher UI complete (with documented limitation)
- ✅ Navigation links working

### Integration
- ✅ End-to-end workflows complete successfully
- ✅ Profile browser → editor → save → browser cycle works
- ✅ Template → customize → save works
- ✅ Export downloads valid JSON

### Security
- ✅ Path traversal attempts blocked
- ✅ Oversized profiles rejected
- ✅ XSS attempts sanitized
- ✅ No SQL injection vulnerabilities

### Regression
- ✅ Existing mount settings page functional
- ✅ Device manager unaffected
- ✅ Telescope control working
- ✅ ASCOM Alpaca API compatible

---

## Next Steps After Testing

### If All Tests Pass:
1. ✅ Mark Phase 4.10 as complete
2. ✅ Update documentation with test results
3. ✅ Create Git tag: `v4.10.0`
4. ✅ Update `Phase-4.10-Completion-Status.md`
5. ✅ Plan Phase 4.11 or Phase 5.x

### If Issues Found:
1. ⚠️ Document issues in bug tracker
2. ⚠️ Prioritize: Critical vs Minor
3. ⚠️ Fix critical issues before release
4. ⚠️ Defer minor issues to Phase 4.10.1 or Phase 4.11
5. ⚠️ Retest after fixes

### Documentation Updates:
1. Update `Phase-4.10-Profile-Management-UI-Plan.md`
   - Mark all tasks complete
   - Add completion date
   - Note any deferred items

2. Create `Phase-4.10-Completion-Status.md`
   - Summary of deliverables
   - Test results
   - Known limitations
   - Future work

3. Update user documentation
   - How to use profile management
   - API documentation for remote clients
   - Troubleshooting guide

---

## Appendix: Test Data

### Sample Profile JSON (for API testing)

```json
{
  "Mount": "SkyWatcher",
  "AlignmentMode": "GermanPolar",
  "Latitude": 51.21135,
  "Longitude": -1.49045,
  "Elevation": 100.0,
  "SiteName": "Test Observatory",
  "ApertureArea": 0.049087,
  "ApertureDiameter": 0.25,
  "FocalLength": 1.5,
  "Handshake": "None",
  "BaudRate": 115200,
  "DTREnable": false,
  "DataBits": 8,
  "Parity": "None",
  "StopBits": "One",
  "ComPort": "COM3",
  "AutoTrack": true,
  "TrackingRate": "Sidereal",
  "GuideRateRightAscensionArcsecPerSec": 15.0,
  "GuideRateDeclinationArcsecPerSec": 15.0
}
```

### Postman Collection (Import JSON)

```json
{
  "info": {
    "name": "GreenSwamp Alpaca - Profile Management API",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "item": [
    {
      "name": "Remote Client API",
      "item": [
        {
          "name": "List All Profiles",
          "request": {
            "method": "GET",
            "header": [],
            "url": {
              "raw": "{{baseUrl}}/api/profiles",
              "host": ["{{baseUrl}}"],
              "path": ["api", "profiles"]
            }
          }
        },
        {
          "name": "Download Single Profile",
          "request": {
            "method": "GET",
            "header": [],
            "url": {
              "raw": "{{baseUrl}}/api/profiles/:name",
              "host": ["{{baseUrl}}"],
              "path": ["api", "profiles", ":name"],
              "variable": [
                {
                  "key": "name",
                  "value": "simulator-altaz"
                }
              ]
            }
          }
        },
        {
          "name": "Check Profile Exists (HEAD)",
          "request": {
            "method": "HEAD",
            "header": [],
            "url": {
              "raw": "{{baseUrl}}/api/profiles/:name",
              "host": ["{{baseUrl}}"],
              "path": ["api", "profiles", ":name"],
              "variable": [
                {
                  "key": "name",
                  "value": "simulator-altaz"
                }
              ]
            }
          }
        },
        {
          "name": "Get Profile Metadata",
          "request": {
            "method": "GET",
            "header": [],
            "url": {
              "raw": "{{baseUrl}}/api/profiles/:name/metadata",
              "host": ["{{baseUrl}}"],
              "path": ["api", "profiles", ":name", "metadata"],
              "variable": [
                {
                  "key": "name",
                  "value": "simulator-altaz"
                }
              ]
            }
          }
        },
        {
          "name": "Upload Profile",
          "request": {
            "method": "PUT",
            "header": [
              {
                "key": "Content-Type",
                "value": "application/json"
              }
            ],
            "body": {
              "mode": "raw",
              "raw": "{\n  \"profile\": {\n    \"Mount\": \"SkyWatcher\",\n    \"AlignmentMode\": \"GermanPolar\",\n    \"Latitude\": 51.21135,\n    \"Longitude\": -1.49045,\n    \"Elevation\": 100.0\n  },\n  \"options\": {\n    \"overwrite\": false,\n    \"validate\": true\n  }\n}"
            },
            "url": {
              "raw": "{{baseUrl}}/api/profiles/:name",
              "host": ["{{baseUrl}}"],
              "path": ["api", "profiles", ":name"],
              "variable": [
                {
                  "key": "name",
                  "value": "test-profile"
                }
              ]
            }
          }
        },
        {
          "name": "Validate Profile",
          "request": {
            "method": "POST",
            "header": [
              {
                "key": "Content-Type",
                "value": "application/json"
              }
            ],
            "body": {
              "mode": "raw",
              "raw": "{\n  \"Mount\": \"SkyWatcher\",\n  \"AlignmentMode\": \"GermanPolar\",\n  \"Latitude\": 95.5,\n  \"Longitude\": -1.49045\n}"
            },
            "url": {
              "raw": "{{baseUrl}}/api/profiles/:name/validate",
              "host": ["{{baseUrl}}"],
              "path": ["api", "profiles", ":name", "validate"],
              "variable": [
                {
                  "key": "name",
                  "value": "validation-test"
                }
              ]
            }
          }
        }
      ]
    }
  ],
  "variable": [
    {
      "key": "baseUrl",
      "value": "https://localhost:5000",
      "type": "string"
    }
  ]
}
```

---

## Contact & Support

**For testing questions or issues found during testing:**
- Document in project issue tracker
- Include test number and steps to reproduce
- Attach screenshots for UI issues
- Include request/response for API issues

**Testing completed by:** _______________  
**Date:** _______________  
**Sign-off:** _______________

---

**Document Version:** 1.0  
**Last Updated:** 2025-01-XX  
**Status:** 🧪 Ready for Testing
