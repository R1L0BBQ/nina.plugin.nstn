using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using Nstn.Core;

namespace NINA.Plugin.Nstn.Conditions {

    /// <summary>
    /// Keeps an instruction set running while the NSTN sky model rates one band as
    /// worth exposing for the target of the enclosing target container.
    ///
    /// NINA checks conditions before every instruction of the set and of every set
    /// nested in it, so the block ends cleanly between two exposures. A running
    /// exposure is deliberately not interrupted: losing a sub costs more than
    /// finishing one a few minutes into a closing window.
    ///
    /// Only Line and BandwidthNm are saved with the sequence. Everything shown in
    /// the sequencer is recomputed, so a template works on any night.
    /// </summary>
    [ExportMetadata("Name", "Loop while NSTN window open")]
    [ExportMetadata("Description", "Runs the instruction set only while the NSTN sky model rates the chosen band as worth exposing for this target. Checked between instructions; a running exposure is finished, never aborted.")]
    [ExportMetadata("Icon", "WaningGibbousMoonSVG")]
    [ExportMetadata("Category", "NSTN")]
    [Export(typeof(ISequenceCondition))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NstnWindowCondition : SequenceCondition, IValidatable, IFollowsLanguage {

        // Display only - the decision itself is always made against the current time.
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

        // NINA calls Validate() before every Check(), and checks every condition
        // once per instruction of its block and of every nested block. Within this
        // span a forecast is reused instead of recomputed; the open/closed answer
        // still uses the current time.
        private static readonly TimeSpan ReuseFor = TimeSpan.FromSeconds(2);

        private readonly IProfileService profileService;
        private readonly object refreshGate = new object();

        private string line = Bands.DefaultKey;
        private double bandwidthNm = Bands.DefaultBandwidthNm;
        private string filterPreset = string.Empty;
        private IList<string> issues = new List<string>();
        private string windowSummary = string.Empty;
        private string nowSummary = string.Empty;
        private bool isOpenNow;
        // Survives the loop's ResetProgress on purpose: the log gets one line per
        // change of state, not one per pass of an outer loop.
        private bool? lastReportedOpen;
        private BandForecast lastForecast;
        private DateTime lastRefreshUtc = DateTime.MinValue;

        [ImportingConstructor]
        public NstnWindowCondition(IProfileService profileService) {
            this.profileService = profileService;
            ConditionWatchdog = new ConditionWatchdog(RefreshFromWatchdog, RefreshInterval);
            PluginLanguage.Follow(profileService);
            PluginLanguage.Register(this, () => Text.T("Name_Condition"), () => Text.T("Desc_Condition"));
        }

        public void LanguageChanged() {
            if (Parent != null) {
                Task.Run(() => Refresh(force: true));
            }
            // N.I.N.A.'s SequenceCondition.Name does not notify; the new name is only
            // shown when the view hears about it.
            RaisePropertyChanged(nameof(Name));
            RaisePropertyChanged(nameof(PresetHint));
        }

        private NstnWindowCondition(NstnWindowCondition cloneMe) : this(cloneMe.profileService) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new NstnWindowCondition(this) {
                Line = Line,
                BandwidthNm = BandwidthNm,
                FilterPreset = FilterPreset,
            };
        }

        [OnDeserialized]
        public void OnDeserialized(StreamingContext context) {
            RunWatchdogIfInsideSequenceRoot();
        }

        // -------------------------------------------------------------------
        // saved with the sequence
        // -------------------------------------------------------------------

        // Line and BandwidthNm are what the condition judges, also when a catalog
        // filter is chosen: a template keeps working if the catalog changes or the
        // name is unknown to an older plugin. They are declared before FilterPreset,
        // so they are written - and read back - before it.

        [JsonProperty]
        public string Line {
            get => line;
            set {
                // A ComboBox whose list is being swapped can push null; that is not a choice.
                if (string.IsNullOrEmpty(value)) {
                    return;
                }
                line = Bands.Normalize(value);
                var preset = FilterCatalog.Find(filterPreset);
                if (preset != null) {
                    var entry = preset.Find(line);
                    if (entry != null) {
                        bandwidthNm = entry.BandwidthNm;
                        RaisePropertyChanged(nameof(BandwidthNm));
                    } else {
                        // A line the filter does not pass: the choice is a custom band now.
                        filterPreset = string.Empty;
                        RaisePresetChanged();
                    }
                }
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsNarrowband));
                RaisePropertyChanged(nameof(ShortLabel));
                RaisePropertyChanged(nameof(PresetHint));
                RaisePropertyChanged(nameof(HasPresetHint));
                ScheduleRefresh();
            }
        }

        [JsonProperty]
        public double BandwidthNm {
            get => bandwidthNm;
            set {
                // A zero or negative passband has no meaning; keep the last valid one.
                if (!(value > 0) || double.IsInfinity(value)) {
                    RaisePropertyChanged();
                    return;
                }
                bandwidthNm = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PresetHint));
                RaisePropertyChanged(nameof(HasPresetHint));
                ScheduleRefresh();
            }
        }

        /// <summary>
        /// Name of the catalog filter the band was taken from, empty for a band set by
        /// hand. Setting it keeps the current line if the filter passes it and takes
        /// the catalog bandwidth; this is the path of a loaded sequence, where the
        /// saved line must survive. A choice in the sequencer goes through
        /// <see cref="SelectedPreset"/> instead.
        /// </summary>
        [JsonProperty]
        public string FilterPreset {
            get => filterPreset;
            set {
                if (value == BlockFilter) {
                    // Band and bandwidth follow the filter of the block; resolved on refresh.
                    filterPreset = BlockFilter;
                    RaisePresetChanged();
                    ScheduleRefresh();
                    return;
                }
                var preset = FilterCatalog.Find(value);
                filterPreset = preset?.Name ?? string.Empty;
                if (preset != null) {
                    var keep = preset.Find(line) ?? preset.WeakLine ?? preset.Lines[0];
                    line = keep.Line;
                    bandwidthNm = keep.BandwidthNm;
                    // The list before the selection, see ResolveBlockFilter.
                    RaisePropertyChanged(nameof(AvailableBands));
                    RaisePropertyChanged(nameof(Line));
                    RaisePropertyChanged(nameof(BandwidthNm));
                    RaisePropertyChanged(nameof(IsNarrowband));
                    RaisePropertyChanged(nameof(ShortLabel));
                }
                RaisePresetChanged();
                ScheduleRefresh();
            }
        }

        // -------------------------------------------------------------------
        // display, never saved
        // -------------------------------------------------------------------

        /// <summary>
        /// Stored in <see cref="FilterPreset"/> for "take the band from the filter of this
        /// block". An older plugin does not know it and falls back to the saved line and
        /// bandwidth, which are always written.
        /// </summary>
        public const string BlockFilter = "@block";

        private static readonly IReadOnlyList<PresetChoice> Choices =
            Presets.All.Take(1)
                .Append(new PresetChoice(BlockFilter, () => Text.T("Preset_BlockFilter")))
                .Concat(Presets.All.Skip(1))
                .ToList();

        public IReadOnlyList<PresetChoice> PresetChoices => Choices;

        private string blockFilterText = string.Empty;

        /// <summary>Which filter the block uses and what it is assigned to; empty unless the band follows the block.</summary>
        public string BlockFilterText {
            get => blockFilterText;
            private set {
                if (blockFilterText == value) {
                    return;
                }
                blockFilterText = value;
                RaisePropertyChanged();
            }
        }

        public bool FollowsBlockFilter => filterPreset == BlockFilter;

        public bool IsBandSelectable => !FollowsBlockFilter;

        /// <summary>
        /// The filter chosen in the sequencer. A fresh choice starts a duo filter on
        /// its weak line - the channel that floods first - and a set on the current
        /// line if it has one.
        /// </summary>
        public string SelectedPreset {
            get => filterPreset;
            set {
                if (value == null) {
                    return;
                }
                var preset = FilterCatalog.Find(value);
                if (preset != null) {
                    line = (preset.WeakLine ?? preset.Find(line) ?? preset.Lines[0]).Line;
                }
                FilterPreset = value;
            }
        }

        /// <summary>
        /// The bands to choose from: only the lines of the chosen catalog filter, else
        /// all of them - also when the band follows the block's filter, where the
        /// selector is locked and the list must not change with every filter switch.
        /// Always an existing list instance, see <see cref="Bands.Of"/>.
        /// </summary>
        public IReadOnlyList<BandChoice> AvailableBands {
            get {
                var preset = FilterCatalog.Find(filterPreset);
                return preset == null ? Bands.All : Bands.Of(preset);
            }
        }

        public bool IsBandwidthEditable => filterPreset.Length == 0;

        /// <summary>A duo filter judged on its robust line: say what that means for the other channel.</summary>
        public string PresetHint {
            get {
                var weak = FilterCatalog.Find(filterPreset)?.WeakLine;
                if (weak == null || weak.Line == line) {
                    return string.Empty;
                }
                var weakLabel = Bands.ShortLabel(weak.Line);
                return Text.T("Cond_DuoHint", ShortLabel, weakLabel);
            }
        }

        public bool HasPresetHint => PresetHint.Length > 0;

        public bool IsNarrowband => Line != Bands.Broadband;

        public string ShortLabel => Bands.ShortLabel(Line);

        private void RaisePresetChanged() {
            RaisePropertyChanged(nameof(FilterPreset));
            RaisePropertyChanged(nameof(SelectedPreset));
            RaisePropertyChanged(nameof(AvailableBands));
            RaisePropertyChanged(nameof(IsBandwidthEditable));
            RaisePropertyChanged(nameof(PresetHint));
            RaisePropertyChanged(nameof(HasPresetHint));
            RaisePropertyChanged(nameof(FollowsBlockFilter));
            RaisePropertyChanged(nameof(IsBandSelectable));
            if (!FollowsBlockFilter) {
                BlockFilterText = string.Empty;
            }
        }

        /// <summary>
        /// "From this block's filter": the filter of the Switch Filter / Smart Exposure
        /// in this block (<paramref name="names"/>, read before refreshGate), looked up
        /// in the profile's assignments. Line and bandwidth are updated - and saved -
        /// only when they actually change. Must not touch N.I.N.A.'s containers.
        /// </summary>
        private void ResolveBlockFilter(List<string> names, NstnOptions options, List<string> found) {
            if (names.Count == 0) {
                BlockFilterText = Text.T("Cond_BlockNone");
                found.Add(Text.T("Cond_IssueBlockNoFilter"));
                return;
            }
            if (names.Count > 1) {
                BlockFilterText = Text.T("Cond_BlockSeveral", string.Join(", ", names));
                found.Add(Text.T("Cond_IssueBlockSeveral", string.Join(", ", names)));
                return;
            }
            var assignment = FilterAssignments.Find(options.Assignments, names[0]);
            if (assignment == null) {
                BlockFilterText = Text.T("Cond_BlockUnassigned", names[0]);
                found.Add(Text.T("Cond_IssueUnassigned", names[0]));
                return;
            }
            BlockFilterText = Text.T("Cond_BlockAssigned", names[0], assignment.Describe());
            var width = assignment.IsBroadband ? bandwidthNm : assignment.BandwidthNm;
            if (line != assignment.Line || bandwidthNm != width) {
                line = assignment.Line;
                bandwidthNm = width;
                // On the UI thread in one go, like a choice made by hand.
                RaiseOnUiThread(nameof(Line), nameof(BandwidthNm), nameof(IsNarrowband), nameof(ShortLabel));
            }
        }

        /// <summary>Notify in this order on the UI thread; queued (never blocking) when called from elsewhere.</summary>
        private void RaiseOnUiThread(params string[] names) {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) {
                foreach (var name in names) {
                    RaisePropertyChanged(name);
                }
                return;
            }
            dispatcher.BeginInvoke(new Action(() => {
                foreach (var name in names) {
                    RaisePropertyChanged(name);
                }
            }));
        }

        private static IEnumerable<string> BlockFilterNames(ISequenceContainer container, int depth) {
            if (container == null || depth > 8) {
                yield break;
            }
            foreach (var item in container.GetItemsSnapshot()) {
                if (item.Status == SequenceEntityStatus.DISABLED) {
                    continue;
                }
                if (item is SwitchFilter switchFilter) {
                    var name = switchFilter.Filter?.Name?.Trim();
                    if (!string.IsNullOrEmpty(name)) {
                        yield return name;
                    }
                } else if (item is ISequenceContainer child) {
                    foreach (var name in BlockFilterNames(child, depth + 1)) {
                        yield return name;
                    }
                }
            }
        }

        // The display setters only notify on a real change: Check() runs many times
        // a second in a loop of skipped blocks, and every notification is UI work.

        /// <summary>Tonight's window for the target, e.g. "Tonight (18 Nov): 19:12–22:48 · 3 h 36 min".</summary>
        public string WindowSummary {
            get => windowSummary;
            private set {
                if (windowSummary == value) {
                    return;
                }
                windowSummary = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>The state right now and when it changes next.</summary>
        public string NowSummary {
            get => nowSummary;
            private set {
                if (nowSummary == value) {
                    return;
                }
                nowSummary = value;
                RaisePropertyChanged();
            }
        }

        public bool IsOpenNow {
            get => isOpenNow;
            private set {
                if (isOpenNow == value) {
                    return;
                }
                isOpenNow = value;
                RaisePropertyChanged();
            }
        }

        public IList<string> Issues {
            get => issues;
            private set {
                if (issues.SequenceEqual(value)) {
                    return;
                }
                issues = value;
                RaisePropertyChanged();
            }
        }

        // -------------------------------------------------------------------
        // sequencer contract
        // -------------------------------------------------------------------

        public override void AfterParentChanged() {
            ScheduleRefresh();
            RunWatchdogIfInsideSequenceRoot();
        }

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) {
            var forecast = Refresh(force: false);
            var open = forecast != null && forecast.IsOpenAt(NightModel.ToEpochMs(NstnClock.UtcNow));
            if (IsActive() && lastReportedOpen != open) {
                lastReportedOpen = open;
                var reason = forecast == null ? string.Join(" ", Issues) : NowSummary;
                // The block's name tells two blocks of the same band apart (a filter plan names them after its rows).
                Logger.Info($"{nameof(NstnWindowCondition)} {ShortLabel} in '{Parent?.Name}' {(open ? "open" : "closed")}: {reason}");
            }
            return open;
        }

        public bool Validate() {
            Refresh(force: false);
            return Issues.Count == 0;
        }

        public override string ToString() {
            return $"Condition: {nameof(NstnWindowCondition)}, Line: {Line}, Bandwidth: {BandwidthNm} nm, Filter: {(FilterPreset.Length > 0 ? FilterPreset : "custom")}, {NowSummary}";
        }

        // -------------------------------------------------------------------
        // the forecast
        // -------------------------------------------------------------------

        /// <summary>The forecast as of now, for the wait instruction. Null when the condition is unusable.</summary>
        internal BandForecast CurrentForecast() => Refresh(force: false);

        private Task RefreshFromWatchdog() {
            Refresh(force: true);
            return Task.CompletedTask;
        }

        // The first night computation takes a few milliseconds; keep it off the UI
        // thread when the condition is dropped or edited.
        private void ScheduleRefresh() => Task.Run(() => Refresh(force: true));

        /// <summary>
        /// Recompute (cheaply, the sky is cached) and update the display. Null when
        /// unusable. Without <paramref name="force"/> a result younger than
        /// <see cref="ReuseFor"/> is returned as it is.
        /// </summary>
        private BandForecast Refresh(bool force) {
            // Everything read from N.I.N.A.'s containers is read here, before refreshGate.
            // N.I.N.A. 3.2 guards all containers with one static lock and calls Validate()
            // of the items while holding it (SequenceContainer.AfterParentChanged); the
            // wait instruction's Validate() asks this condition, so the UI thread can hold
            // that lock and wait for refreshGate. Taking the container lock inside
            // refreshGate (GetItemsSnapshot) from a background refresh then deadlocks both:
            // on loading a sequence with a block filter, N.I.N.A. stopped taking input.
            var blockFilters = FollowsBlockFilter
                ? BlockFilterNames(Parent, 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : null;
            lock (refreshGate) {
                if (!force && NstnClock.UtcNow - lastRefreshUtc < ReuseFor) {
                    ShowNow(lastForecast);
                    return lastForecast;
                }
                var found = new List<string>();
                BandForecast forecast = null;
                try {
                    var options = new NstnOptions(profileService);
                    var context = ItemUtility.RetrieveContextCoordinates(Parent);
                    if (context == null) {
                        found.Add(Text.T("Cond_IssueNoTarget"));
                    }
                    if (!options.HasSkyQuality) {
                        found.Add(Text.T("Issue_NoSqm"));
                    }
                    if (blockFilters != null) {
                        ResolveBlockFilter(blockFilters, options, found);
                    }
                    if (found.Count == 0) {
                        var astrometry = profileService.ActiveProfile.AstrometrySettings;
                        var j2000 = context.Coordinates.Transform(Epoch.J2000);
                        forecast = WindowForecaster.Shared.Forecast(
                            NstnClock.UtcNow,
                            TimeZoneInfo.Local,
                            astrometry.Latitude,
                            astrometry.Longitude,
                            j2000.RADegrees,
                            j2000.Dec,
                            options.SkyQuality,
                            Bands.ToFilter(Line, BandwidthNm),
                            options.ToWindowSettings());
                        // A band that never opens at this site is shown, not reported as
                        // an issue: NINA validates before every check (RunCheck) and turns
                        // a failed validation into a FAILED status plus a failure event -
                        // before every instruction, which notification plugins pass on.
                        // An empty window only skips the block, which is correct.
                    }
                } catch (Exception ex) {
                    Logger.Error(ex);
                    found.Add(Text.T("Cond_IssueFailed", ex.Message));
                    forecast = null;
                }

                Issues = found;
                ShowForecast(forecast);
                lastForecast = forecast;
                lastRefreshUtc = NstnClock.UtcNow;
                return forecast;
            }
        }

        private void ShowForecast(BandForecast forecast) {
            if (forecast == null) {
                WindowSummary = string.Empty;
            } else {
                var night = Text.Date(forecast.NightDate);
                if (forecast.NeverOpensAtSite) {
                    // Broadband keeps its bandwidth field but does not use it, and its own threshold applies.
                    WindowSummary = IsNarrowband
                        ? Text.T("Cond_NeverAtSite", ShortLabel, BandwidthNm, forecast.MinimumSkyBrightness, forecast.SqmZenith)
                        : Text.T("Cond_NeverAtSiteBroadband", ShortLabel, forecast.MinimumSkyBrightness, forecast.SqmZenith);
                } else if (forecast.Segments.Count == 0) {
                    WindowSummary = Text.T("Cond_TonightNever", night);
                } else {
                    var spans = string.Join(", ", forecast.Segments.Select(s => $"{Clock(s.Start)}–{Clock(s.End)}"));
                    WindowSummary = Text.T("Cond_Tonight", night, spans, Text.Duration(forecast.OpenMinutes));
                }
            }
            ShowNow(forecast);
        }

        /// <summary>The part of the display that moves with the clock.</summary>
        private void ShowNow(BandForecast forecast) {
            if (forecast == null) {
                NowSummary = Text.T("Cond_NoForecast");
                IsOpenNow = false;
                return;
            }

            var now = NightModel.ToEpochMs(NstnClock.UtcNow);
            var open = forecast.IsOpenAt(now);
            var change = forecast.NextChangeAfter(now);
            var state = open
                ? (change == null ? Text.T("Cond_Open") : Text.T("Cond_OpenUntil", Clock(change.Value)))
                : (change == null ? Text.T("Cond_ClosedRest") : Text.T("Cond_ClosedOpens", Clock(change.Value)));
            var magnitude = forecast.InBandMagnitudeAt(now);
            NowSummary = magnitude == null
                ? state
                : Text.T("Cond_SkyInBand", state, magnitude.Value, forecast.Threshold);
            IsOpenNow = open;
        }

        private static string Clock(double epochMs) => Text.Clock(NightModel.ToUtcDateTime(epochMs).ToLocalTime());
    }
}
