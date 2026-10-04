using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.Components.Dialogs;
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

        protected override void OnInitialized()
        {
            _mount = MountRegistry.GetInstance(DeviceNumber);
            _isMountRunning = _mount?.IsMountRunning ?? false;
            _horizonLimit = _mount?.Settings.HorizonLimit ?? 0.0;
            _altDMin = (int)Math.Floor(_horizonLimit);
            _canSync = _mount?.Settings.CanSync ?? false;
        }

        private async Task OnGoTo()
        {
            _isMountRunning = _mount?.IsMountRunning ?? false;
            if (_mount == null || !_isMountRunning)
            {
                Snackbar.Add("Mount is not running.", Severity.Error);
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
                Snackbar.Add("Target coordinates are below the mount's horizon limit.", Severity.Warning);
                return;
            }

            // Show confirmation dialog
            var parameters = new DialogParameters
            {
                [nameof(AcceptCoordinatesDialog.Title)] = "Accept GoTo Coordinates",
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
                    Snackbar.Add("GoTo in progress\u2026", Severity.Info);
                else
                    Snackbar.Add($"GoTo rejected: {slewResult.ErrorMessage}", Severity.Warning);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"GoTo failed: {ex.Message}", Severity.Error);
            }
        }

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
                Snackbar.Add("Mount is not running.", Severity.Error);
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
                [nameof(AcceptCoordinatesDialog.Title)] = "Accept Sync Coordinates",
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
                Snackbar.Add("Sync complete.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Sync failed: {ex.Message}", Severity.Error);
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

        private const string RequiredMessage = "Value is required.";

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

        private static string FormatRaSmart(double hours)
        {
            hours = Math.Max(0.0, hours);
            var h = (int)hours;
            var rem = (hours - h) * 60.0;
            var m = (int)rem;
            var s = (rem - m) * 60.0;
            return $"{h:00}h {m:00}m {s:00.##}s";
        }

        private static string FormatAngleSmart(double degrees)
        {
            var sign = degrees < 0 ? "-" : "+";
            degrees = Math.Abs(degrees);
            var d = (int)degrees;
            var rem = (degrees - d) * 60.0;
            var m = (int)rem;
            var s = (rem - m) * 60.0;
            return $"{sign}{d:00}° {m:00}′ {s:00.##}″";
        }

        private static bool TryParseSmartRa(string input, out double hours, out string error)
        {
            return TryParseSmartCoordinate(input, out hours, out error, isRa: true);
        }

        private static bool TryParseSmartAngle(string input, out double degrees, out string error)
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

        private static string? ValidateSmartPartial(string input, bool isRa)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                // Empty is not an error while editing; "required" is only reported on GoTo / Sync.
                return string.Empty;
            }

            var trimmed = input.Trim();
            if (!isRa && (trimmed == "+" || trimmed == "-"))
            {
                return string.Empty;
            }

            var decimalMode = TryParseSmartDecimalMode(trimmed, isRa, out var decimalCanonical);
            if (decimalMode)
            {
                return string.IsNullOrEmpty(decimalCanonical) ? (isRa ? "Hours must be in the range [0..24)." : "Degrees must be in the range [-90..90].") : string.Empty;
            }

            var tokens = TokenizeSmartInput(input);
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            if (!TryParseIntegerToken(tokens[0], out var major))
            {
                return isRa ? "Hours must be an integer." : "Degrees must be an integer.";
            }

            var sign = 1;
            if (!isRa && trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                sign = -1;
            }

            var signedMajor = major * sign;
            if (isRa)
            {
                if (signedMajor < 0 || signedMajor >= 24) return "Hours must be in the range [0..24).";
            }
            else
            {
                if (signedMajor < -90 || signedMajor > 90) return "Degrees must be in the range [-90..90].";
            }

            if (tokens.Length > 1 && (!TryParseIntegerToken(tokens[1], out var minutes) || minutes < 0 || minutes >= 60))
            {
                return "Minutes must be an integer in the range [0..60).";
            }

            if (tokens.Length > 2 && (!TryParseDecimalToken(tokens[2], out var seconds) || seconds < 0 || seconds >= 60))
            {
                return "Seconds must be in the range [0..60).";
            }

            return string.Empty;
        }

        private static string CanonicalizeSmartValue(string input, bool isRa, bool canonicalizeSign)
        {
            var trimmed = input.Trim();
            if (!isRa && (trimmed == "+" || trimmed == "-"))
            {
                return trimmed;
            }

            var decimalMode = TryParseSmartDecimalMode(trimmed, isRa, out var decimalCanonical);
            if (decimalMode && !string.IsNullOrEmpty(decimalCanonical))
            {
                return decimalCanonical;
            }

            var tokens = TokenizeSmartInput(input);
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            if (!TryParseIntegerToken(tokens[0], out var major))
            {
                return input.Trim();
            }

            var minutes = 0;
            var seconds = 0.0;
            if (tokens.Length > 1)
            {
                _ = int.TryParse(tokens[1], out minutes);
            }
            if (tokens.Length > 2)
            {
                _ = double.TryParse(tokens[2], out seconds);
            }

            if (isRa)
            {
                return tokens.Length > 2
                    ? $"{major:00}h {minutes:00}m {seconds:00.##}s"
                    : tokens.Length > 1
                        ? $"{major:00}h {minutes:00}m"
                        : $"{major:00}h";
            }

            var sign = string.Empty;
            if (canonicalizeSign)
            {
                if (input.TrimStart().StartsWith("-", StringComparison.Ordinal)) sign = "-";
                else if (input.TrimStart().StartsWith("+", StringComparison.Ordinal)) sign = "+";
            }

            var signedMajor = sign == "-" ? -Math.Abs(major) : Math.Abs(major);
            return tokens.Length > 2
                ? $"{signedMajor:+00;-00;00}° {minutes:00}′ {seconds:00.##}″"
                : tokens.Length > 1
                    ? $"{signedMajor:+00;-00;00}° {minutes:00}′"
                    : $"{signedMajor:+00;-00;00}°";
        }

        private static bool TryParseSmartDecimalMode(string input, bool isRa, out string canonical)
        {
            canonical = string.Empty;
            if (!input.Contains('.'))
            {
                return false;
            }

            var text = input.Trim();
            var sign = string.Empty;
            if (!isRa && (text.StartsWith("+") || text.StartsWith("-")))
            {
                sign = text.StartsWith("-") ? "-" : "+";
                text = text[1..].TrimStart();
            }

            if (!double.TryParse(text, out var value))
            {
                return false;
            }

            if (isRa)
            {
                if (value < 0 || value >= 24)
                {
                    return true;
                }

                canonical = $"{value:0.##}";
                return true;
            }

            if (sign == "-") value = -value;
            if (value < -90 || value > 90)
            {
                return true;
            }

            canonical = $"{value:+0.##;-0.##;0.##}";
            return true;
        }

        private static string[] TokenizeSmartInput(string input)
        {
            var normalized = input.Trim()
                .Replace("º", "°")
                .Replace('’', '′')
                .Replace('\'', '′')
                .Replace('"', '″')
                .Replace(':', ' ')
                .Replace("°", " ")
                .Replace("h", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("m", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("s", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("′", " ")
                .Replace("″", " ");

            return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryParseIntegerToken(string token, out int value)
        {
            return int.TryParse(token, out value);
        }

        private static bool TryParseDecimalToken(string token, out double value)
        {
            return double.TryParse(token, out value);
        }

        private static bool TryParseSmartCoordinate(string input, out double value, out string error, bool isRa)
        {
            value = 0;
            error = isRa ? "Enter a valid RA value." : "Enter a valid angle value.";

            if (string.IsNullOrWhiteSpace(input))
            {
                error = isRa ? "RA is required." : "Angle value is required.";
                return false;
            }

            var trimmed = input.Trim();
            if (TryParseSmartDecimalMode(trimmed, isRa, out var decimalCanonical))
            {
                if (string.IsNullOrEmpty(decimalCanonical))
                {
                    error = isRa ? "RA must be in the range [0..24)." : "Degrees must be in the range [-90..90].";
                    return false;
                }

                if (!double.TryParse(trimmed.TrimStart('+', '-'), out value))
                {
                    error = isRa ? "Enter a valid RA value." : "Enter a valid angle value.";
                    return false;
                }

                if (!isRa && trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    value = -value;
                }

                if (isRa)
                {
                    if (value < 0 || value >= 24)
                    {
                        error = "RA must be in the range [0..24).";
                        return false;
                    }
                }
                else if (value < -90 || value > 90)
                {
                    error = "Degrees must be in the range [-90..90].";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            var normalized = input.Trim()
                .Replace("°", " ")
                .Replace("º", " ")
                .Replace("h", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("m", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("s", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("′", " ")
                .Replace("’", " ")
                .Replace("'", " ")
                .Replace("″", " ")
                .Replace("\"", " ")
                .Replace(":", " ");

            var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 3)
            {
                return false;
            }

            var sign = 1.0;
            if (!isRa && parts[0].StartsWith("-", StringComparison.Ordinal))
            {
                sign = -1.0;
            }

            if (!int.TryParse(parts[0], out var a)) return false;
            var b = 0;
            var c = 0.0;

            if (parts.Length > 1 && !int.TryParse(parts[1], out b)) return false;
            if (parts.Length > 2 && !double.TryParse(parts[2], out c)) return false;

            var major = Math.Abs(a);
            if (parts.Length > 1 && (b < 0 || b >= 60))
            {
                error = "Minutes must be in the range [0..60).";
                return false;
            }

            if (parts.Length > 2 && (c < 0 || c >= 60))
            {
                error = "Seconds must be in the range [0..60).";
                return false;
            }

            value = major + b / 60.0 + c / 3600.0;
            if (!isRa)
            {
                value *= sign;
            }

            error = string.Empty;
            return true;
        }
    }
}
