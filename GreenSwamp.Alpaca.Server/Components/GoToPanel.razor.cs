using ASCOM;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.Components.Dialogs;
using GreenSwamp.Alpaca.Server.Helpers;
using GreenSwamp.Alpaca.Server.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components
{
    public partial class GoToPanel
    {
        [Parameter, EditorRequired] public TelescopeStateModel State { get; set; } = new();
        [Parameter] public int DeviceNumber { get; set; }
        [Parameter] public bool IsUiClientConnected { get; set; }

        private enum CoordMode { RaDec, AltAz }

        private GreenSwamp.Alpaca.MountControl.Mount? _mount;
        private bool _isMountRunning;
        private double _horizonLimit;
        private int _altDMin;
        private bool _canSync;

        private CoordMode _coordMode = CoordMode.RaDec;

        private string _raSmart = string.Empty;
        private string _decSmart = string.Empty;
        private string _azSmart = string.Empty;
        private string _altSmart = string.Empty;

        private string? _raSmartError;
        private string? _decSmartError;
        private string? _azSmartError;
        private string? _altSmartError;
        private bool _isSettingsPanelOpen;
        private bool _isSettingsDirty;
        private bool _showTrackingLimitSettings;
        private bool _showHorizonLimitSettings;
        private string _initialParkPositionName = "—";
        private bool _initialVoiceActive;
        private LimitAction _initialHorizonTrackingAction;
        private LimitAction _initialMeridianTrackingAction;
        private double _initialFlipAngle;
        private double _initialTrackingLimitAfterFlipAngle;
        private double _initialHorizonTrackingLimit;
        private double _initialHorizonGoToLimit;
        private string _workingParkPositionName = "—";
        private double _workingFlipAngle;
        private double _workingTrackingLimitAfterFlipAngle;
        private double _workingHorizonTrackingLimit;
        private double _workingHorizonGoToLimit;
        private bool IsParkSelectorDisabled => !IsUiClientConnected || !(_mount?.Settings.CanPark ?? false);
        private string EffectiveParkPositionName =>
            (State.ParkPositionNames.Count > 0 && State.ParkPositionNames.Contains(State.ParkSelectedName))
                ? State.ParkSelectedName!
                : State.ParkPositionNames.FirstOrDefault(p => string.Equals(p, "Default", StringComparison.OrdinalIgnoreCase))
                    ?? State.ParkPositionNames.FirstOrDefault()
                    ?? "—";
        private LimitAction _workingMeridianTrackingAction;
        private LimitAction _workingHorizonTrackingAction;
        private bool _workingSyncLimitOn;

        private bool _workingVoiceActive;

        protected override void OnInitialized()
        {
            _mount = MountRegistry.GetInstance(DeviceNumber);
            _isMountRunning = _mount?.IsMountRunning ?? false;
            _horizonLimit = _mount?.Settings.HorizonLimit ?? 0.0;
            _altDMin = (int)Math.Floor(_horizonLimit);
            _canSync = _mount?.Settings.CanSync ?? false;
            _initialHorizonTrackingAction = _mount?.Settings.HorizonTrackingAction ?? LimitAction.None;
            _initialMeridianTrackingAction = _mount?.Settings.MeridianTrackingAction ?? LimitAction.None;
        }

        private async Task OnGoTo()
        {
            _isMountRunning = _mount?.IsMountRunning ?? false;
            if (_mount == null || !_isMountRunning)
            {
                Snackbar.Add(Loc["GoTo_MountNotRunning"], Severity.Error);
                return;
            }

            // Resolve target coordinates from active entry fields only
            double coord1;
            double coord2;
            string parseError;
            if (_coordMode == CoordMode.RaDec)
            {
                if (!TryGetRaDecCoordinates(out coord1, out coord2, out parseError))
                {
                    Snackbar.Add(parseError, Severity.Warning);
                    return;
                }
            }
            else
            {
                if (!TryGetAltAzCoordinates(out coord1, out coord2, out parseError))
                {
                    Snackbar.Add(parseError, Severity.Warning);
                    return;
                }
            }

            double[] AltAz;
            if (_coordMode == CoordMode.RaDec)
            {
                AltAz = Coordinate.RaDec2AltAz(coord1, coord2, _mount.SiderealTime, _mount.Settings.Latitude);
            }
            else
            {
                AltAz = [coord2, coord1]; // signature: (alt, az)
            }

            // Check if the target coordinates are below the mount's horizon limit
            if (AltAz[0] < _horizonLimit)
            {
                Snackbar.Add(Loc["GoTo_BelowHorizon"], Severity.Warning);
                return;
            }

            // Show confirmation dialog
            var parameters = new DialogParameters
            {
                [nameof(AcceptCoordinatesDialog.Title)] = Loc["GoTo_AcceptTitle"].Value,
                [nameof(AcceptCoordinatesDialog.Coord1)] = coord1,
                [nameof(AcceptCoordinatesDialog.Coord2)] = coord2,
                [nameof(AcceptCoordinatesDialog.IsRaDec)] = _coordMode == CoordMode.RaDec
            };
            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.ExtraSmall,
                CloseOnEscapeKey = true,
                BackdropClick = false
            };
            var dialog = await DialogService.ShowAsync<AcceptCoordinatesDialog>("", parameters, options);
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            // Execute slew
            try
            {
                SlewResult slewResult;
                if (_coordMode == CoordMode.RaDec)
                    slewResult = await _mount.SlewRaDecAsync(coord1, coord2, tracking: true);
                else
                    slewResult = await _mount.SlewAltAzAsync(coord2, coord1);   // signature: (alt, az)

                if (slewResult.CanProceed)
                    Snackbar.Add(Loc["GoTo_InProgress"], Severity.Info);
                else
                    Snackbar.Add(Loc["GoTo_Rejected", slewResult.ErrorMessage], Severity.Warning);
            }
            catch (Exception ex)
            {
                Snackbar.Add(Loc["GoTo_Failed", ex.Message], Severity.Error);
            }
        }

        private void OpenSettingsPanel()
        {
            _initialParkPositionName = EffectiveParkPositionName;
            _initialVoiceActive = State.VoiceActive;
            _initialHorizonTrackingAction = _mount.Settings?.HorizonTrackingAction ?? LimitAction.None;
            _initialMeridianTrackingAction = _mount.Settings?.MeridianTrackingAction ?? LimitAction.None;
            _initialFlipAngle = _mount.Settings?.HourAngleLimit ?? 0;
            _initialTrackingLimitAfterFlipAngle = _mount.Settings?.AxisTrackingLimit ?? 0;
            _initialHorizonTrackingLimit = _mount.Settings?.AxisHzTrackingLimit ?? 0;
            _initialHorizonGoToLimit = _mount.Settings?.HorizonLimit ?? 0;

            _workingParkPositionName = _initialParkPositionName;
            _workingVoiceActive = _initialVoiceActive;
            _workingMeridianTrackingAction = _initialMeridianTrackingAction;
            _workingHorizonTrackingAction = _initialHorizonTrackingAction;
            _workingFlipAngle = _initialFlipAngle;
            _workingTrackingLimitAfterFlipAngle = _initialTrackingLimitAfterFlipAngle;
            _workingHorizonTrackingLimit = _initialHorizonTrackingLimit;
            _workingHorizonGoToLimit = _initialHorizonGoToLimit;
            _workingSyncLimitOn = _mount.Settings?.SyncLimitOn ?? false;
            _showTrackingLimitSettings = false;
            _showHorizonLimitSettings = false;

            _isSettingsDirty = false;
            _isSettingsPanelOpen = true;
        }

        private void CloseSettingsPanelWithoutSave()
        {
            _isSettingsPanelOpen = false;
        }

        private void CancelSettingsChanges()
        {
            _workingParkPositionName = _initialParkPositionName;
            _workingVoiceActive = _initialVoiceActive;
            _workingMeridianTrackingAction = _initialMeridianTrackingAction;
            _workingHorizonTrackingAction = _initialHorizonTrackingAction;
            _workingFlipAngle = _initialFlipAngle;
            _workingTrackingLimitAfterFlipAngle = _initialTrackingLimitAfterFlipAngle;
            _workingHorizonTrackingLimit = _initialHorizonTrackingLimit;
            _workingHorizonGoToLimit = _initialHorizonGoToLimit;
            _showTrackingLimitSettings = false;
            _showHorizonLimitSettings = false;
            _isSettingsDirty = false;
            _isSettingsPanelOpen = false;
        }

        private Task SaveSettingsChangesAsync()
        {
            if (_mount == null)
            {
                Snackbar.Add(Loc["Common_MountNotFound", DeviceNumber], Severity.Error);
                return Task.CompletedTask;
            }

            if (!string.Equals(_workingParkPositionName, _initialParkPositionName, StringComparison.Ordinal))
            {
                var selectedPosition = _mount.Settings.ParkPositions?.Find(p => p.Name == _workingParkPositionName);
                if (selectedPosition == null)
                {
                    Snackbar.Add(Loc["GoTo_ParkNotFound", _workingParkPositionName], Severity.Warning);
                    return Task.CompletedTask;
                }

                _mount.ParkSelected = selectedPosition;
            }

            if (_workingVoiceActive != _initialVoiceActive)
            {
                _mount.Settings.VoiceActive = _workingVoiceActive;
            }

            if (_workingMeridianTrackingAction != _initialMeridianTrackingAction)
            {
                _mount.Settings.MeridianTrackingAction = _workingMeridianTrackingAction;
            }

            if (_workingHorizonTrackingAction != _initialHorizonTrackingAction)
            {
                _mount.Settings.HorizonTrackingAction = _workingHorizonTrackingAction;
            }

            if (!NearlyEqual(_workingFlipAngle, _initialFlipAngle))
            {
                _mount.Settings.HourAngleLimit = _workingFlipAngle;
            }

            if (!NearlyEqual(_workingTrackingLimitAfterFlipAngle, _initialTrackingLimitAfterFlipAngle))
            {
                _mount.Settings.AxisTrackingLimit = _workingTrackingLimitAfterFlipAngle;
            }

            if (!NearlyEqual(_workingHorizonTrackingLimit, _initialHorizonTrackingLimit))
            {
                _mount.Settings.AxisHzTrackingLimit = _workingHorizonTrackingLimit;
            }

            if (!NearlyEqual(_workingHorizonGoToLimit, _initialHorizonGoToLimit))
            {
                _mount.Settings.HorizonLimit = _workingHorizonGoToLimit;
                _horizonLimit = _workingHorizonGoToLimit;
            }

            _initialParkPositionName = _workingParkPositionName;
            _initialVoiceActive = _workingVoiceActive;
            _initialMeridianTrackingAction = _workingMeridianTrackingAction;
            _initialHorizonTrackingAction = _workingHorizonTrackingAction;
            _initialFlipAngle = _workingFlipAngle;
            _initialTrackingLimitAfterFlipAngle = _workingTrackingLimitAfterFlipAngle;
            _initialHorizonTrackingLimit = _workingHorizonTrackingLimit;
            _initialHorizonGoToLimit = _workingHorizonGoToLimit;
            _showTrackingLimitSettings = false;
            _showHorizonLimitSettings = false;
            _isSettingsDirty = false;
            _isSettingsPanelOpen = false;
            Snackbar.Add(Loc["GoTo_SettingsSaved"], Severity.Success);
            return Task.CompletedTask;
        }

        private void OnParkPositionDraftSelected(string positionName)
        {
            _workingParkPositionName = positionName;
            UpdateSettingsDirtyState();
        }

        private async Task OpenManageParkPositionsDialogFromPanelAsync()
        {
            var parameters = new DialogParameters
            {
                [nameof(ManageParkPositionsDialog.DeviceNumber)] = DeviceNumber
            };
            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.Small,
                FullWidth = false,
                CloseOnEscapeKey = true,
                CloseButton = true
            };
            await DialogService.ShowAsync<ManageParkPositionsDialog>(Loc["ParkPos_Title"], parameters, options);
        }

        private Task UpdateVoiceActive(bool value)
        {
            _workingVoiceActive = value;
            UpdateSettingsDirtyState();
            return Task.CompletedTask;
        }

        private Task ToggleTrackingLimitSettings()
        {
            _showTrackingLimitSettings = !_showTrackingLimitSettings;
            return Task.CompletedTask;
        }

        private Task ToggleHorizonLimitSettings()
        {
            _showHorizonLimitSettings = !_showHorizonLimitSettings;
            return Task.CompletedTask;
        }

        private Task UpdateMeridianLimitAction(LimitAction value)
        {
            _workingMeridianTrackingAction = value;
            UpdateSettingsDirtyState();
            return Task.CompletedTask;
        }

        private Task UpdateHorizonLimitAction(LimitAction value)
        {
            _workingHorizonTrackingAction = value;
            UpdateSettingsDirtyState();
            return Task.CompletedTask;
        }

        private Task OnFlipAngleChanged(double value)
        {
            _workingFlipAngle = value;
            UpdateSettingsDirtyState();
            return Task.CompletedTask;
        }

        private Task OnTrackingLimitAfterFlipAngleChanged(double value)
        {
            _workingTrackingLimitAfterFlipAngle = value;
            UpdateSettingsDirtyState();
            return Task.CompletedTask;
        }

        private Task OnHorizonTrackingLimitChanged(double value)
        {
            _workingHorizonTrackingLimit = value;
            UpdateSettingsDirtyState();
            return Task.CompletedTask;
        }

        private Task OnHorizonGoToLimitChanged(double value)
        {
            _workingHorizonGoToLimit = value;
            UpdateSettingsDirtyState();
            return Task.CompletedTask;
        }

        private Task OnSettingsPrimaryActionClicked()
        {
            if (_isSettingsDirty)
            {
                return SaveSettingsChangesAsync();
            }

            CloseSettingsPanelWithoutSave();
            return Task.CompletedTask;
        }

        private void UpdateSettingsDirtyState()
        {
            _isSettingsDirty = !string.Equals(_workingParkPositionName, _initialParkPositionName, StringComparison.Ordinal)
                || _workingVoiceActive != _initialVoiceActive
                || _initialHorizonTrackingAction != _workingHorizonTrackingAction
                || _initialMeridianTrackingAction != _workingMeridianTrackingAction
                || !NearlyEqual(_workingFlipAngle, _initialFlipAngle)
                || !NearlyEqual(_workingTrackingLimitAfterFlipAngle, _initialTrackingLimitAfterFlipAngle)
                || !NearlyEqual(_workingHorizonTrackingLimit, _initialHorizonTrackingLimit)
                || !NearlyEqual(_workingHorizonGoToLimit, _initialHorizonGoToLimit);
        }

        /// <summary>
        /// Determines if two double values are nearly equal, within a small tolerance.
        /// </summary>
        /// <param name="left">The first double value to compare.</param>
        /// <param name="right">The second double value to compare.</param>
        /// <returns>True if the values are nearly equal; otherwise, false.</returns>
        private static bool NearlyEqual(double left, double right)
            => Math.Abs(left - right) < 0.0001d;

        /// <summary>Copies the current live mount position into all entry fields.</summary>
        private void OnCopy()
        {
            var state = StateService.GetCurrentState(DeviceNumber);

            _raSmart = FormatRaSmart(state.RightAscension);
            _decSmart = FormatAngleSmart(state.Declination);
            _azSmart = FormatAngleSmart(state.Azimuth);
            _altSmart = FormatAngleSmart(state.Altitude);

            _raSmartError = null;
            _decSmartError = null;
            _azSmartError = null;
            _altSmartError = null;
        }

        /// <summary>Syncs the mount to the entered RA/Dec coordinates.</summary>
        private async Task OnSync()
        {
            _isMountRunning = _mount?.IsMountRunning ?? false;
            if (_mount == null || !_isMountRunning)
            {
                Snackbar.Add(Loc["GoTo_MountNotRunning"], Severity.Error);
                return;
            }

            // Resolve target coordinates from active entry fields only
            double coord1;
            double coord2;
            string parseError;
            if (_coordMode == CoordMode.RaDec)
            {
                if (!TryGetRaDecCoordinates(out coord1, out coord2, out parseError))
                {
                    Snackbar.Add(parseError, Severity.Warning);
                    return;
                }
            }
            else
            {
                if (!TryGetAltAzCoordinates(out coord1, out coord2, out parseError))
                {
                    Snackbar.Add(parseError, Severity.Warning);
                    return;
                }
            }

            // Show confirmation dialog
            var parameters = new DialogParameters
            {
                [nameof(AcceptCoordinatesDialog.Title)] = Loc["GoTo_AcceptSyncTitle"].Value,
                [nameof(AcceptCoordinatesDialog.Coord1)] = coord1,
                [nameof(AcceptCoordinatesDialog.Coord2)] = coord2,
                [nameof(AcceptCoordinatesDialog.IsRaDec)] = _coordMode == CoordMode.RaDec
            };
            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.ExtraSmall,
                CloseOnEscapeKey = true,
                BackdropClick = false
            };
            var dialog = await DialogService.ShowAsync<AcceptCoordinatesDialog>("", parameters, options);
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            try
            {
                _mount.TargetRa = coord1;
                _mount.TargetDec = coord2;
                await Task.Run(() => _mount.SyncToTargetRaDec());
                Snackbar.Add(Loc["GoTo_SyncComplete"], Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add(Loc["GoTo_SyncFailed", ex.Message], Severity.Error);
            }
        }

        private bool TryGetRaDecCoordinates(out double ra, out double dec, out string error)
        {
            ra = 0;
            dec = 0;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(_raSmart)) _raSmartError = RequiredMessage;
            if (string.IsNullOrWhiteSpace(_decSmart)) _decSmartError = RequiredMessage;
            if (!TryParseSmartRa(_raSmart, out ra, out error)) return false;
            if (!TryParseSmartAngle(_decSmart, out dec, out error)) return false;
            return true;
        }

        private string RequiredMessage => Loc["Coord_Required"];

        private bool TryGetAltAzCoordinates(out double az, out double alt, out string error)
        {
            az = 0;
            alt = 0;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(_azSmart)) _azSmartError = RequiredMessage;
            if (string.IsNullOrWhiteSpace(_altSmart)) _altSmartError = RequiredMessage;
            if (!TryParseSmartAngle(_azSmart, out az, out error)) return false;
            if (!TryParseSmartAngle(_altSmart, out alt, out error)) return false;
            return true;
        }

        private static string FormatRaSmart(double hours) => CoordinateParser.FormatRa(hours);

        private static string FormatAngleSmart(double degrees) => CoordinateParser.FormatAngle(degrees);

        private bool TryParseSmartRa(string input, out double hours, out string error)
        {
            return TryParseSmartCoordinate(input, out hours, out error, isRa: true);
        }

        private bool TryParseSmartAngle(string input, out double degrees, out string error)
        {
            return TryParseSmartCoordinate(input, out degrees, out error, isRa: false);
        }

        private Task OnRaSmartChanged(string? value) => ApplySmartInputAsync(value, v => _raSmart = v, isRa: true, setError: e => _raSmartError = e);
        private Task OnDecSmartChanged(string? value) => ApplySmartInputAsync(value, v => _decSmart = v, isRa: false, setError: e => _decSmartError = e);
        private Task OnAzSmartChanged(string? value) => ApplySmartInputAsync(value, v => _azSmart = v, isRa: false, setError: e => _azSmartError = e);
        private Task OnAltSmartChanged(string? value) => ApplySmartInputAsync(value, v => _altSmart = v, isRa: false, setError: e => _altSmartError = e);

        private Task OnRaSmartBlur() => ApplySmartBlurAsync(v => _raSmart = v, _raSmart, isRa: true, canonicalizeSign: false, setError: e => _raSmartError = e);
        private Task OnDecSmartBlur() => ApplySmartBlurAsync(v => _decSmart = v, _decSmart, isRa: false, canonicalizeSign: true, setError: e => _decSmartError = e);
        private Task OnAzSmartBlur() => ApplySmartBlurAsync(v => _azSmart = v, _azSmart, isRa: false, canonicalizeSign: false, setError: e => _azSmartError = e);
        private Task OnAltSmartBlur() => ApplySmartBlurAsync(v => _altSmart = v, _altSmart, isRa: false, canonicalizeSign: true, setError: e => _altSmartError = e);

        private Task ApplySmartInputAsync(string? incoming, Action<string> assign, bool isRa, Action<string?> setError)
        {
            var next = incoming ?? string.Empty;
            assign(next);
            setError(ValidateSmartPartial(next, isRa));
            return Task.CompletedTask;
        }

        private Task ApplySmartBlurAsync(Action<string> assign, string current, bool isRa, bool canonicalizeSign, Action<string?> setError)
        {
            var canonical = CanonicalizeSmartValue(current, isRa, canonicalizeSign);
            assign(canonical);
            setError(ValidateSmartPartial(canonical, isRa));
            return Task.CompletedTask;
        }

        private string? ValidateSmartPartial(string input, bool isRa)
        {
            return CoordinateParser.ValidatePartial(input, isRa) switch
            {
                CoordinateParseError.NotInteger => isRa ? Loc["Coord_HoursInteger"].Value : Loc["Coord_DegreesInteger"].Value,
                CoordinateParseError.OutOfRange => isRa ? Loc["Coord_HoursRange"].Value : Loc["Coord_DegreesRange"].Value,
                CoordinateParseError.MinutesNotInteger => Loc["Coord_MinutesInteger"].Value,
                CoordinateParseError.SecondsRange => Loc["Coord_SecondsRange"].Value,
                _ => string.Empty
            };
        }

        private static string CanonicalizeSmartValue(string input, bool isRa, bool canonicalizeSign)
        {
            return CoordinateParser.Canonicalize(input, isRa, canonicalizeSign);
        }

        private bool TryParseSmartCoordinate(string input, out double value, out string error, bool isRa)
        {
            if (CoordinateParser.TryParse(input, isRa, out value, out var parseError))
            {
                error = string.Empty;
                return true;
            }

            error = parseError switch
            {
                CoordinateParseError.Required => isRa ? Loc["Coord_RaRequired"].Value : Loc["Coord_AngleRequired"].Value,
                CoordinateParseError.OutOfRange => isRa ? Loc["Coord_RaRange"].Value : Loc["Coord_DegreesRange"].Value,
                CoordinateParseError.MinutesRange => Loc["Coord_MinutesRange"].Value,
                CoordinateParseError.SecondsRange => Loc["Coord_SecondsRange"].Value,
                _ => isRa ? Loc["Coord_InvalidRa"].Value : Loc["Coord_InvalidAngle"].Value
            };
            return false;
        }
    }
}
