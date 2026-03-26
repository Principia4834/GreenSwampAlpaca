# Settings System Error Handling - Update Summary

**Date:** 2026-03-25  
**Status:** Requirements and Design Updated  
**Next Steps:** Code implementation deferred pending user review

---

## Overview

The Settings System Requirements and High-Level Design documents have been updated to reflect a comprehensive error handling strategy that prioritizes graceful degradation, clear error reporting, and automatic repair capabilities.

---

## Key Principles

### 1. **Graceful Degradation**
- Application continues running even when settings files have errors
- Blazor UI remains accessible for configuration management
- Future REST API will remain functional for programmatic error resolution
- Invalid devices are quarantined, not advertised

### 2. **Never Advertise Broken Devices**
- Devices with configuration problems are NOT included in `/management/v1/configureddevices`
- Invalid devices are NOT advertised via Alpaca Discovery protocol
- Invalid devices are NOT instantiated in device registry
- Validation performed before device activation

### 3. **Clear Error Communication**
- Errors reported with specific problem descriptions
- Actionable resolution steps provided for each error
- Settings file path always displayed for manual intervention
- Error severity levels (FATAL, ERROR, WARNING, INFO) guide user response

### 4. **Self-Healing Capabilities**
- Automatic repair actions for common configuration problems
- Backup created before any repair operation
- Repair results validated before accepting changes
- Failed repairs restore from backup automatically

---

## Requirements Updates (Settings-System-Requirements.md)

### New Functional Requirements

**FR-17: Graceful Error Handling**
- Application must remain accessible via UI and REST API with settings errors
- Error states clearly reported through both interfaces
- User can access settings management to fix problems

**FR-18: Device Validation and Quarantine**
- Invalid devices not advertised or activated
- Comprehensive validation rules defined:
  - DeviceNumber uniqueness
  - Required properties present
  - Enum values valid
  - Array synchronization
- Validation errors logged with specific guidance

**FR-19: Settings File Validation**
- JSON syntax validation
- Required sections present (Devices, AlpacaDevices arrays)
- 1-to-1 array synchronization
- File-level vs device-level error distinction

**FR-20: Error Reporting via UI**
- Dedicated "Settings Health" page/section
- Error indicators on device management pages
- Specific error messages with resolution steps
- One-click repair actions

**FR-21: Error Reporting via REST API (Future)**
- Design accommodates future endpoints:
  - `GET /api/settings/health` - Overall status
  - `GET /api/settings/validation` - Detailed validation
  - `GET /api/settings/devices/{deviceNumber}/status` - Device status
  - `POST /api/settings/repair` - Trigger repair
  - `DELETE /api/settings/reset` - Reset to defaults
- Structured error responses with HTTP status codes
- Service layer ready for API integration

**FR-22: Automatic Repair Actions**
- "Repair Settings" action via UI and future REST API
- Supported repairs:
  - Regenerate AlpacaDevices array
  - Assign missing DeviceNumbers
  - Remove orphaned entries
  - Fix name synchronization
  - Generate missing UniqueIds
- Non-destructive where possible
- Backup before repair, restore on failure

### Updated Non-Functional Requirements

**NFR-2: Reliability**
- Missing settings file: Create defaults automatically
- Corrupt settings file: Application continues, errors reported
- Invalid device: Quarantine device, report via UI/API
- Migration failure: Fall back to defaults, report status

**NFR-3: Usability**
- Error messages include resolution steps
- Settings health status prominently displayed
- One-click repair for common problems
- Settings file path always shown
- Future REST API for programmatic access

---

## Design Updates (Settings-System-HighLevel-Design.md)

### New Section: Error Handling Architecture (Section 5)

**5.1 Error Handling Philosophy**
- Defines five core principles (graceful degradation, never advertise broken, clear communication, fail-fast validation, self-healing)

**5.2 Error Classification**
- Mermaid diagram showing error severity levels, categories, and handling strategies
- Maps error types to response strategies (Continue, Quarantine, Repair, Manual)

**5.3 Validation Pipeline**
- Comprehensive validation flow diagram (Mermaid)
- Five-phase validation:
  1. File existence check
  2. JSON syntax validation
  3. Structural validation (arrays present)
  4. Synchronization validation (arrays match)
  5. Device-level validation (properties valid)
- Quarantine invalid devices, report all errors

**5.4 Error Codes and Handling**
- Standardized error code format: `{CATEGORY}_{SPECIFIC_PROBLEM}`
- Complete error code tables:
  - **File-level:** FILE_NOT_FOUND, FILE_PARSE_ERROR, FILE_ACCESS_DENIED, FILE_LOCKED
  - **Structural:** MISSING_DEVICES_ARRAY, EMPTY_DEVICES_ARRAY, MISSING_ALPACA_ARRAY, INVALID_ARRAY_TYPE
  - **Synchronization:** ARRAY_COUNT_MISMATCH, DEVICE_NOT_IN_ALPACA, ALPACA_NOT_IN_DEVICES, DEVICE_NAME_MISMATCH, MISSING_UNIQUE_ID
  - **Device-level:** DUPLICATE_DEVICE_NUMBER, NEGATIVE_DEVICE_NUMBER, MISSING_DEVICE_NAME, INVALID_ALIGNMENT_MODE, INVALID_MOUNT_TYPE
- Each error includes: severity, description, resolution steps, auto-repair capability

**5.5 Service Layer Integration**
- Enhanced `IVersionedSettingsService` interface with error handling methods
- `GetAllDevices(out ValidationResult)` overload for validation results
- `ValidationResult` and `RepairResult` classes for structured error reporting
- Example implementation showing all validation phases

**5.6 UI Integration Points**
- Settings Health Component specification
- Device Management Grid enhancements
- Server/Monitor Settings page updates
- Error display patterns with examples

**5.7 REST API Design (Future Implementation Ready)**
- Endpoint specifications documented
- Example API responses provided
- HTTP status code mapping defined
- Service layer methods designed to support future API

---

## Error Code Reference

### Quick Reference Table

| Error Code | Severity | Auto-Repair | Description |
|------------|----------|-------------|-------------|
| **File-Level Errors** |
| FILE_NOT_FOUND | INFO | ✅ | User settings don't exist, create defaults |
| FILE_PARSE_ERROR | ERROR | ❌ | JSON syntax invalid, manual fix required |
| FILE_ACCESS_DENIED | ERROR | ❌ | Permission problem, check file security |
| FILE_LOCKED | WARNING | ⏱️ | Retry after other process releases file |
| **Structural Errors** |
| MISSING_DEVICES_ARRAY | ERROR | ✅ | Regenerate from defaults |
| EMPTY_DEVICES_ARRAY | WARNING | ✅ | Add default device automatically |
| MISSING_ALPACA_ARRAY | WARNING | ✅ | Regenerate from Devices array |
| INVALID_ARRAY_TYPE | ERROR | ✅ | Regenerate array structure |
| **Synchronization Errors** |
| ARRAY_COUNT_MISMATCH | ERROR | ✅ | Rebuild AlpacaDevices from Devices |
| DEVICE_NOT_IN_ALPACA | ERROR | ✅ | Add missing AlpacaDevices entry |
| ALPACA_NOT_IN_DEVICES | ERROR | ✅ | Remove orphaned AlpacaDevices entry |
| DEVICE_NAME_MISMATCH | WARNING | ✅ | Sync names from Devices to AlpacaDevices |
| MISSING_UNIQUE_ID | WARNING | ✅ | Generate GUID for entry |
| **Device-Level Errors** |
| DUPLICATE_DEVICE_NUMBER | ERROR | ❌ | Remove duplicate manually |
| NEGATIVE_DEVICE_NUMBER | ERROR | ❌ | Fix DeviceNumber manually |
| MISSING_DEVICE_NAME | ERROR | ✅ | Generate default name |
| INVALID_ALIGNMENT_MODE | ERROR | ✅ | Reset to GermanPolar |
| INVALID_MOUNT_TYPE | ERROR | ✅ | Reset to Simulator |

---

## Implementation Considerations

### Service Layer Changes Required

1. **VersionedSettingsService**
   - Add `ValidationResult` and `RepairResult` classes
   - Implement `GetAllDevices(out ValidationResult)` overload
   - Implement `ValidateDevice()` private method
   - Implement `RepairSettingsAsync()` method
   - Add `CreateBackup()` helper method

2. **DeviceSynchronizationService**
   - Enhance `ValidateSynchronization()` to return `ValidationResult`
   - Add detailed error/warning generation
   - Implement repair helper methods

3. **IVersionedSettingsService Interface**
   - Add error handling method signatures
   - Maintain backward compatibility with existing methods

### UI Components to Create/Update

1. **New: SettingsHealthStatus.razor Component**
   - Display overall validation status
   - List errors and warnings with resolution steps
   - Provide "Repair Settings" button
   - Show repair progress and results

2. **Update: Device Management Grid**
   - Add validation status column
   - Show error indicators
   - Display error details on hover/click

3. **Update: Server/Monitor Settings Pages**
   - Display validation warnings
   - Highlight invalid properties

### REST API (Future Phase)

- Service layer is designed to support future REST endpoints
- `ValidationResult` and `RepairResult` classes are API-ready (JSON serializable)
- Error codes standardized for HTTP response bodies
- No immediate implementation required

---

## Validation Flow Example

**Scenario: User settings file has synchronization error**

```
1. Application startup calls GetAllDevices()

2. File validation: PASS
   - File exists
   - Valid JSON syntax
   - Devices array present with 3 devices
   - AlpacaDevices array present with 2 devices

3. Synchronization validation: FAIL
   - Error: DEVICE_NOT_IN_ALPACA
   - DeviceNumber 2 exists in Devices but not AlpacaDevices

4. Device-level validation: PASS
   - All device properties valid

5. Result:
   - ValidationResult.IsValid = false
   - ValidationResult.Errors = [DEVICE_NOT_IN_ALPACA for device 2]
   - Valid devices: Device 0, Device 1 (returned and activated)
   - Invalid devices: Device 2 (quarantined, not activated)

6. UI displays:
   - ❌ Settings validation failed with 1 error
   - Error details: "Device 2 exists in Devices but not AlpacaDevices"
   - Resolution: "Use 'Repair Settings' to add missing AlpacaDevices entry"
   - [Repair Settings] button enabled

7. User clicks "Repair Settings":
   - Backup created: appsettings.user.json.backup-20260325-153045
   - AlpacaDevices entry added for device 2
   - Settings file updated
   - Validation re-run: PASS
   - UI displays: ✅ Settings repaired successfully. 1 action performed.
   - Device 2 will activate after app restart
```

---

## Migration from Current Implementation

### Current State (Pre-Error Handling)

- `GetAllDevices()` throws exceptions on invalid settings
- Application startup fails if settings file corrupt
- No validation of AlpacaDevices/Devices synchronization
- No repair capabilities
- Limited error reporting

### Future State (Post-Error Handling)

- `GetAllDevices()` returns valid devices, reports errors separately
- Application always starts (creates defaults if needed)
- Comprehensive validation at file, structural, sync, and device levels
- Automatic repair for most common errors
- Rich error reporting via UI and future REST API

### Breaking Changes

**None - Backward Compatible**

- Existing `GetAllDevices()` method preserved (returns only valid devices)
- New `GetAllDevices(out ValidationResult)` overload added for error details
- Existing code continues to work, just gets filtered devices
- UI updates are additive (new components, enhanced existing pages)

---

## Testing Strategy

### Unit Tests Required

1. **Validation Tests**
   - Each error code path (20+ test cases)
   - Valid settings baseline
   - Missing file, corrupt JSON, structural errors
   - Synchronization mismatches
   - Device-level property validation

2. **Repair Tests**
   - Auto-repairable errors fixed correctly
   - Backup created before repair
   - Backup restored on repair failure
   - Non-repairable errors left for manual intervention

3. **Quarantine Tests**
   - Invalid devices not included in returned list
   - Invalid devices not registered in device registry
   - Invalid devices not advertised via Alpaca

### Integration Tests Required

1. **Startup Tests**
   - Application starts with missing settings
   - Application starts with corrupt settings
   - Application starts with valid settings

2. **UI Tests**
   - Settings Health component displays errors correctly
   - Repair button triggers repair and updates UI
   - Device grid shows validation status

3. **Multi-Device Tests**
   - Mixture of valid and invalid devices
   - Only valid devices activated
   - Error reporting shows all problems

---

## Next Steps

### Phase 1: Review (Current)
- ✅ Requirements document updated
- ✅ Design document updated
- ⏳ **USER ACTION:** Review both documents offline

### Phase 2: Implementation (After Approval)
1. Update service interfaces and models
2. Implement validation logic in VersionedSettingsService
3. Enhance DeviceSynchronizationService validation
4. Implement repair methods
5. Create SettingsHealthStatus.razor component
6. Update Device Management grid
7. Write unit tests
8. Write integration tests

### Phase 3: REST API (Future)
- Define OpenAPI/Swagger spec
- Implement API controllers
- Add API error response middleware
- Document API endpoints
- API integration tests

---

## Questions for User Review

1. **Error Code Coverage:** Are there additional error scenarios we should handle?

2. **Auto-Repair Scope:** Should any currently manual errors become auto-repairable?

3. **UI Placement:** Should Settings Health be:
   - Dedicated page in navigation?
   - Section on Device Management page?
   - Dashboard widget?
   - All of the above?

4. **Notification Strategy:** Should the app show notifications when:
   - Settings validation fails at startup?
   - Devices are quarantined?
   - Repair succeeds/fails?

5. **REST API Priority:** Should REST API implementation happen sooner than "future phase"?

6. **Logging Detail:** Current design uses MonitorLog pattern. Should validation errors also:
   - Write to separate validation log file?
   - Trigger email/webhook notifications?
   - Integrate with external monitoring tools?

---

## Document References

- **[Settings-System-Requirements.md](./Settings-System-Requirements.md)** - Updated requirements with FR-17 through FR-22
- **[Settings-System-HighLevel-Design.md](./Settings-System-HighLevel-Design.md)** - Updated design with Section 5 (Error Handling Architecture)

---

**End of Summary**
