using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Newtonsoft.Json;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using Nstn.Core;

namespace NINA.Plugin.Nstn.FilterPlan {

    /// <summary>
    /// One row of the filter plan: a filter as it is named in the profile, how many
    /// exposures per round and how long each one is. With the plan values the only
    /// thing the user edits; the instructions of the plan are built from them.
    ///
    /// Optionally the row's own window: priority over the round robin, a stricter or
    /// looser threshold, its own minimum altitude and a time span. They are written
    /// only when set, so a plan without them is saved exactly as before.
    ///
    /// The filter "L/RGB" makes the row a set of four filters sharing the broadband
    /// window: per round L, R, G and B in a ratio such as 3:1:1:1, L with
    /// <see cref="ExposureSeconds"/>, R, G and B with <see cref="RgbExposureSeconds"/>.
    /// For priority the set counts as one row, so its short window is not lost between
    /// the narrowband rows.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FilterPlanRow : BaseINPC {

        /// <summary>The filter name that makes a row an L/RGB set; no profile filter is called that.</summary>
        public const string Lrgb = "L/RGB";

        private const string DefaultRatio = "1:1:1:1";

        private string filterName = string.Empty;
        private int count = 5;
        private double exposureSeconds = 300;
        private string ratio;
        private double rgbExposureSeconds;
        private int? gain;
        private int? offset;
        private string binning;
        private int? rgbGain;
        private int? rgbOffset;
        private string rgbBinning;
        private bool priority;
        private double thresholdOffset;
        private double? minAltitude;
        private string notBefore;
        private string notAfter;
        private bool isExpanded;

        public FilterPlanRow() {
            ToggleWindowCommand = new ActionCommand(() => IsExpanded = !IsExpanded);
        }

        [JsonProperty]
        public string FilterName {
            get => filterName;
            set {
                // A ComboBox whose list is being swapped can push null; that is not a choice.
                if (value == null) {
                    return;
                }
                filterName = value.Trim();
                if (IsLrgb) {
                    // Chosen in the list: a set needs a ratio and a time for R, G and B.
                    ratio ??= DefaultRatio;
                    if (rgbExposureSeconds <= 0) {
                        rgbExposureSeconds = exposureSeconds;
                    }
                } else {
                    // A single filter again: nothing of the set is kept or saved.
                    ratio = null;
                    rgbExposureSeconds = 0;
                    rgbGain = null;
                    rgbOffset = null;
                    rgbBinning = null;
                }
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsLrgb));
                RaisePropertyChanged(nameof(IsSingleFilter));
                RaisePropertyChanged(nameof(Ratio));
                RaisePropertyChanged(nameof(RgbExposureSeconds));
                RaisePropertyChanged(nameof(RgbGain));
                RaisePropertyChanged(nameof(RgbOffset));
                RaisePropertyChanged(nameof(RgbBinning));
                RaisePropertyChanged(nameof(CameraText));
                RaisePropertyChanged(nameof(WindowText));
            }
        }

        public bool IsLrgb => string.Equals(filterName, Lrgb, StringComparison.OrdinalIgnoreCase);

        public bool IsSingleFilter => !IsLrgb;

        /// <summary>
        /// L:R:G:B exposures per round of an L/RGB row, e.g. "3:1:1:1"; 0 leaves a filter
        /// out. Unreadable text keeps the old ratio. Null for a single filter.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Ratio {
            get => ratio;
            set {
                var parts = ParseRatio(value);
                if (parts != null) {
                    ratio = string.Join(":", parts);
                }
                RaisePropertyChanged();
            }
        }

        /// <summary>Exposure time of R, G and B in an L/RGB row; L takes <see cref="ExposureSeconds"/>.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public double RgbExposureSeconds {
            get => rgbExposureSeconds;
            set {
                if (value > 0 && !double.IsInfinity(value)) {
                    rgbExposureSeconds = value;
                }
                RaisePropertyChanged();
            }
        }

        /// <summary>Exposures per round of L, R, G and B; four zeros for a single filter.</summary>
        public IReadOnlyList<int> RatioParts => ParseRatio(ratio) ?? new[] { 0, 0, 0, 0 };

        /// <summary>"3:1:1:1", "3/1/1/1" or "3 1 1 1": four whole numbers 0-99, at least one above 0.</summary>
        internal static int[] ParseRatio(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return null;
            }
            var parts = text.Split(new[] { ':', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) {
                return null;
            }
            var numbers = new int[4];
            for (var i = 0; i < 4; i++) {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] > 99) {
                    return null;
                }
            }
            return numbers.Sum() > 0 ? numbers : null;
        }

        [JsonProperty]
        public int Count {
            get => count;
            set {
                // Keep the last valid value; a text box shows it again.
                if (value >= 1) {
                    count = value;
                }
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double ExposureSeconds {
            get => exposureSeconds;
            set {
                if (value > 0 && !double.IsInfinity(value)) {
                    exposureSeconds = value;
                }
                RaisePropertyChanged();
            }
        }

        /// <summary>The block keeps its window while it is open instead of one turn per round; it runs first.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Priority {
            get => priority;
            set => SetWindow(ref priority, value);
        }

        /// <summary>Added to the site's threshold in mag/arcsec²: more is stricter, a shorter window.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public double ThresholdOffset {
            get => thresholdOffset;
            set => SetWindow(ref thresholdOffset, double.IsFinite(value) && Math.Abs(value) <= 5 ? Math.Round(value, 2) : thresholdOffset);
        }

        /// <summary>The target's minimum altitude for this filter in degrees; null keeps the site's.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public double? MinAltitude {
            get => minAltitude;
            set => SetWindow(ref minAltitude, value is double v && (!double.IsFinite(v) || v < 0 || v > 90) ? minAltitude : value);
        }

        /// <summary>Local clock time the window opens at the earliest ("HH:mm"); null for none.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string NotBefore {
            get => notBefore;
            set => SetWindow(ref notBefore, Clock(value, notBefore));
        }

        /// <summary>Local clock time the window closes at the latest ("HH:mm"); null for none.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string NotAfter {
            get => notAfter;
            set => SetWindow(ref notAfter, Clock(value, notAfter));
        }

        // -------------------------------------------------------------------
        // the row's own camera values; null takes the plan's ("for all exposures")
        // An L/RGB row: Gain/Offset/Binning for L, the Rgb* ones for R, G and B.
        // -------------------------------------------------------------------

        /// <summary>Gain of this row's exposures; -1 the camera's own, null the plan's.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? Gain {
            get => gain;
            set => SetWindow(ref gain, value is int v && v < -1 ? gain : value);
        }

        /// <summary>Offset of this row's exposures; -1 the camera's own, null the plan's.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? Offset {
            get => offset;
            set => SetWindow(ref offset, value is int v && v < -1 ? offset : value);
        }

        /// <summary>Binning of this row's exposures as "2x2"; empty or null the plan's.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Binning {
            get => binning;
            set => SetWindow(ref binning, BinningText(value, binning));
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? RgbGain {
            get => rgbGain;
            set => SetWindow(ref rgbGain, value is int v && v < -1 ? rgbGain : value);
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? RgbOffset {
            get => rgbOffset;
            set => SetWindow(ref rgbOffset, value is int v && v < -1 ? rgbOffset : value);
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string RgbBinning {
            get => rgbBinning;
            set => SetWindow(ref rgbBinning, BinningText(value, rgbBinning));
        }

        /// <summary>The binning of a series as N.I.N.A.'s value, null when the plan's applies.</summary>
        public BinningMode BinningOf(bool rgb) {
            var text = rgb ? rgbBinning : binning;
            if (text == null) {
                return null;
            }
            var parts = text.Split('x');
            return new BinningMode(short.Parse(parts[0], CultureInfo.InvariantCulture), short.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        /// <summary>"2x2", "2X2" or "2" give "2x2"; empty clears it; unreadable text keeps the old value.</summary>
        private static string BinningText(string text, string old) {
            if (string.IsNullOrWhiteSpace(text)) {
                return null;
            }
            var parts = text.Trim().ToLowerInvariant().Split('x');
            if (parts.Length is < 1 or > 2
                    || !short.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x)
                    || !short.TryParse(parts.Length == 2 ? parts[1] : parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                    || x is < 1 or > 16 || y is < 1 or > 16) {
                return old;
            }
            return FormattableString.Invariant($"{x}x{y}");
        }

        /// <summary>The row's own camera values in a few words, empty when it has none.</summary>
        public string CameraText {
            get {
                if (!IsLrgb) {
                    return CameraParts(gain, offset, binning);
                }
                var parts = new List<string>();
                var l = CameraParts(gain, offset, binning);
                if (l.Length > 0) {
                    parts.Add(Text.T("Plan_RowCameraL", l));
                }
                var rgb = CameraParts(rgbGain, rgbOffset, rgbBinning);
                if (rgb.Length > 0) {
                    parts.Add(Text.T("Plan_RowCameraRgb", rgb));
                }
                return string.Join(" · ", parts);
            }
        }

        private static string CameraParts(int? gain, int? offset, string binning) {
            var parts = new List<string>();
            if (gain is int g) {
                parts.Add(g == -1 ? Text.T("Plan_RowGainCamera") : Text.T("Plan_RowGain", g));
            }
            if (offset is int o) {
                parts.Add(o == -1 ? Text.T("Plan_RowOffsetCamera") : Text.T("Plan_RowOffset", o));
            }
            if (binning != null) {
                parts.Add(binning);
            }
            return string.Join(", ", parts);
        }

        /// <summary>Whether the row's own values are shown; not saved.</summary>
        public bool IsExpanded {
            get => isExpanded;
            set {
                isExpanded = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(WindowButtonText));
            }
        }

        /// <summary>A plain button, styled by N.I.N.A. (a ToggleButton is not and stays unreadable on its theme).</summary>
        public ICommand ToggleWindowCommand { get; }

        public string WindowButtonText => (isExpanded ? "▾ " : "▸ ") + Text.T("Plan_Adjust");

        /// <summary>The texts built from the language tables; none of them is saved, so no rebuild.</summary>
        internal void LanguageChanged() {
            RaisePropertyChanged(nameof(WindowButtonText));
            RaisePropertyChanged(nameof(WindowText));
            RaisePropertyChanged(nameof(LimitsText));
            RaisePropertyChanged(nameof(CameraText));
        }

        public WindowLimits Limits => new WindowLimits(thresholdOffset, minAltitude, WindowLimits.ParseClock(notBefore), WindowLimits.ParseClock(notAfter));

        public bool HasOwnWindow => priority || !Limits.IsNone;

        /// <summary>The row's own values in a few words - priority, window, camera - empty when it has none.</summary>
        public string WindowText =>
            string.Join(" · ", new[] { priority ? Text.T("Plan_RowPriority") : string.Empty, LimitsText, CameraText }.Where(p => p.Length > 0));

        /// <summary>Its limits without the priority, for the outline.</summary>
        public string LimitsText {
            get {
                var parts = new List<string>();
                if (thresholdOffset > 0) {
                    parts.Add(Text.T("Plan_RowStricter", thresholdOffset));
                } else if (thresholdOffset < 0) {
                    parts.Add(Text.T("Plan_RowLooser", -thresholdOffset));
                }
                if (minAltitude is double altitude) {
                    parts.Add(Text.T("Plan_RowAltitude", altitude));
                }
                if (notBefore != null) {
                    parts.Add(Text.T("Plan_RowNotBefore", notBefore));
                }
                if (notAfter != null) {
                    parts.Add(Text.T("Plan_RowNotAfter", notAfter));
                }
                return string.Join(" · ", parts);
            }
        }

        /// <summary>Only the saved values: a change rebuilds the plan, opening the settings does not.</summary>
        public static bool IsSaved(string propertyName) => propertyName is nameof(FilterName) or nameof(Count) or nameof(ExposureSeconds)
            or nameof(Ratio) or nameof(RgbExposureSeconds)
            or nameof(Priority) or nameof(ThresholdOffset) or nameof(MinAltitude) or nameof(NotBefore) or nameof(NotAfter)
            or nameof(Gain) or nameof(Offset) or nameof(Binning) or nameof(RgbGain) or nameof(RgbOffset) or nameof(RgbBinning);

        public FilterPlanRow Copy() => new FilterPlanRow {
            ExposureSeconds = ExposureSeconds,
            RgbExposureSeconds = RgbExposureSeconds,
            Ratio = Ratio,
            RgbGain = RgbGain,
            RgbOffset = RgbOffset,
            RgbBinning = RgbBinning,
            FilterName = FilterName,
            Gain = Gain,
            Offset = Offset,
            Binning = Binning,
            Count = Count,
            Priority = Priority,
            ThresholdOffset = ThresholdOffset,
            MinAltitude = MinAltitude,
            NotBefore = NotBefore,
            NotAfter = NotAfter,
        };

        public string Label {
            get {
                if (!IsLrgb) {
                    return $"{FilterName}: {Count} × {Seconds(ExposureSeconds)} s";
                }
                // "L/RGB: 3:1:1:1 (L 60 s, RGB 120 s)"; without L only the RGB time.
                var times = RatioParts[0] > 0
                    ? $"L {Seconds(ExposureSeconds)} s, RGB {Seconds(RgbExposureSeconds)} s"
                    : $"RGB {Seconds(RgbExposureSeconds)} s";
                return $"{Lrgb}: {ratio ?? DefaultRatio} ({times})";
            }
        }

        private static string Seconds(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>The text of a time box: empty clears it, unreadable text keeps the old value.</summary>
        private static string Clock(string text, string old) {
            if (string.IsNullOrWhiteSpace(text)) {
                return null;
            }
            return WindowLimits.ParseClock(text)?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? old;
        }

        private void SetWindow<T>(ref T field, T value, [CallerMemberName] string name = null) {
            var changed = !EqualityComparer<T>.Default.Equals(field, value);
            field = value;
            // Also when unchanged: a text box with an invalid entry shows the kept value again.
            RaisePropertyChanged(name);
            if (changed) {
                RaisePropertyChanged(nameof(WindowText));
                RaisePropertyChanged(nameof(LimitsText));
                RaisePropertyChanged(nameof(CameraText));
                RaisePropertyChanged(nameof(HasOwnWindow));
            }
        }
    }

    /// <summary>A button command with a parameter, always executable.</summary>
    internal sealed class ParameterCommand<T> : ICommand {

        private readonly Action<T> action;

        public ParameterCommand(Action<T> action) {
            this.action = action;
        }

        public event EventHandler CanExecuteChanged {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) {
            if (parameter is T value) {
                action(value);
            }
        }
    }
}
