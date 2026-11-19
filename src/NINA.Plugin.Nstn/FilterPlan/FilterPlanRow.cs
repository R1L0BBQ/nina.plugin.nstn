using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Newtonsoft.Json;
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
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FilterPlanRow : BaseINPC {

        private string filterName = string.Empty;
        private int count = 5;
        private double exposureSeconds = 300;
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
                RaisePropertyChanged();
            }
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

        /// <summary>Whether the row's own window settings are shown; not saved.</summary>
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
        }

        public WindowLimits Limits => new WindowLimits(thresholdOffset, minAltitude, WindowLimits.ParseClock(notBefore), WindowLimits.ParseClock(notAfter));

        public bool HasOwnWindow => priority || !Limits.IsNone;

        /// <summary>The row's own window in a few words, empty when it has none.</summary>
        public string WindowText => priority
            ? string.Join(" · ", new[] { Text.T("Plan_RowPriority"), LimitsText }.Where(p => p.Length > 0))
            : LimitsText;

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
            or nameof(Priority) or nameof(ThresholdOffset) or nameof(MinAltitude) or nameof(NotBefore) or nameof(NotAfter);

        public FilterPlanRow Copy() => new FilterPlanRow {
            FilterName = FilterName,
            Count = Count,
            ExposureSeconds = ExposureSeconds,
            Priority = Priority,
            ThresholdOffset = ThresholdOffset,
            MinAltitude = MinAltitude,
            NotBefore = NotBefore,
            NotAfter = NotAfter,
        };

        public string Label => $"{FilterName}: {Count} × {ExposureSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s";

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
