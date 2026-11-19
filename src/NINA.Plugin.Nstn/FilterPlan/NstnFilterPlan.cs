using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Input;
using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Nstn.Conditions;
using NINA.Plugin.Nstn.Instructions;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using Nstn.Core;

namespace NINA.Plugin.Nstn.FilterPlan {

    /// <summary>
    /// The NSTN round robin as one instruction, like Smart Exposure hides its loop:
    /// the user edits a table (filter, count, exposure time) and a few values for the
    /// whole plan, the container builds the pattern from them -
    ///
    ///   [this, condition "Loop while an NSTN window opens tonight"]
    ///     Wait for NSTN window
    ///     per row: block [NSTN window "@block", Loop For Iterations = rounds per filter]
    ///                Switch Filter (row's filter)
    ///                series [Loop For Iterations = count]  Take Exposure (row's time, plan's gain/offset/binning)
    ///
    /// The rows and plan values are the only source: every edit rebuilds the
    /// instructions. A loaded sequence keeps N.I.N.A.'s own children if they match,
    /// else they are rebuilt (hand-edited file). N.I.N.A. does not look into an
    /// immutable container, so the plan validates its children itself.
    ///
    /// No lock of its own (deadlock rule): N.I.N.A.'s containers have one static lock.
    /// </summary>
    [ExportMetadata("Name", "NSTN filter plan")]
    [ExportMetadata("Description", "One row per filter: its name as in the profile, exposures per round and exposure time. Waits for the first NSTN window, then exposes each filter while its window is open, round after round, until no window opens again tonight.")]
    [ExportMetadata("Icon", "FW_NoFill_SVG")]
    [ExportMetadata("Category", "NSTN")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NstnFilterPlan : SequentialContainer, IImmutableContainer, IFollowsLanguage {

        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IImageHistoryVM imageHistoryVM;
        private readonly IFilterWheelMediator filterWheelMediator;

        // Set while rows and children arrive one by one (constructor, loading a file):
        // nothing is rebuilt until both are complete.
        private bool loading;

        // Exposures of this plan whose status is watched (with the status seen last), and
        // how many failed in a row.
        private readonly Dictionary<TakeExposure, SequenceEntityStatus> watchedExposures = new Dictionary<TakeExposure, SequenceEntityStatus>();
        private int failedInARow;

        [ImportingConstructor]
        public NstnFilterPlan(
                IProfileService profileService,
                ICameraMediator cameraMediator,
                IImagingMediator imagingMediator,
                IImageSaveMediator imageSaveMediator,
                IImageHistoryVM imageHistoryVM,
                IFilterWheelMediator filterWheelMediator)
            : this(null, profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM, filterWheelMediator,
                   DefaultRows(profileService)) {
        }

        private NstnFilterPlan(
                NstnFilterPlan cloneMe,
                IProfileService profileService,
                ICameraMediator cameraMediator,
                IImagingMediator imagingMediator,
                IImageSaveMediator imageSaveMediator,
                IImageHistoryVM imageHistoryVM,
                IFilterWheelMediator filterWheelMediator,
                IEnumerable<FilterPlanRow> rows) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.filterWheelMediator = filterWheelMediator;

            AddRowCommand = new ActionCommand(AddRow);
            ToggleOutlineCommand = new ActionCommand(() => ShowOutline = !ShowOutline);
            RemoveRowCommand = new ParameterCommand<FilterPlanRow>(row => Rows.Remove(row));
            PropertyChanged += OnOwnPropertyChanged;

            loading = true;
            Rows = new ObservableCollection<FilterPlanRow>();
            Rows.CollectionChanged += OnRowsChanged;
            foreach (var row in rows) {
                Rows.Add(row);
            }
            loading = false;

            if (cloneMe != null) {
                CopyMetaData(cloneMe);
                gain = cloneMe.gain;
                offset = cloneMe.offset;
                binning = new BinningMode(cloneMe.binning.X, cloneMe.binning.Y);
                roundsPerFilter = cloneMe.roundsPerFilter;
                stopAfterFailures = cloneMe.stopAfterFailures;
            }
            PluginLanguage.Follow(profileService);
            PluginLanguage.Register(this, () => Text.T("Name_Plan"), () => Text.T("Desc_Plan"));
            Rebuild();
        }

        /// <summary>Drag and drop, templates and loading all go through here (factory template → clone).</summary>
        public override object Clone() {
            return new NstnFilterPlan(this, profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM,
                filterWheelMediator, Rows.Select(r => r.Copy()).ToList());
        }

        public void LanguageChanged() {
            RaisePropertyChanged(nameof(Outline));
            RaisePropertyChanged(nameof(OutlineButtonText));
            foreach (var row in Rows) {
                row.LanguageChanged();
            }
            if (Parent != null) {
                Validate();
            }
        }

        // -------------------------------------------------------------------
        // the table, saved with the sequence
        // -------------------------------------------------------------------

        [JsonProperty]
        public ObservableCollection<FilterPlanRow> Rows { get; private set; }

        public ICommand AddRowCommand { get; }

        private bool showOutline;

        /// <summary>Whether the outline of the steps is shown; folded by default, not saved.</summary>
        public bool ShowOutline {
            get => showOutline;
            private set {
                showOutline = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(OutlineButtonText));
            }
        }

        public ICommand ToggleOutlineCommand { get; }

        public string OutlineButtonText => (showOutline ? "▾ " : "▸ ") + Text.T("Plan_Steps");

        public ICommand RemoveRowCommand { get; }

        /// <summary>Editing while the plan runs would swap the instructions under the strategy.</summary>
        public bool IsEditable => Status != SequenceEntityStatus.RUNNING;

        public IReadOnlyList<string> FilterNames => ProfileFilters().Select(f => f.Name).ToList();

        /// <summary>The choices of a row: an L/RGB set, then the profile's filters.</summary>
        public IReadOnlyList<string> RowChoices => new[] { FilterPlanRow.Lrgb }.Concat(FilterNames).ToList();

        private static IEnumerable<FilterPlanRow> DefaultRows(IProfileService profileService) {
            var filters = profileService?.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
            if (filters == null) {
                return Enumerable.Empty<FilterPlanRow>();
            }
            return filters.Where(f => !string.IsNullOrWhiteSpace(f?.Name)).Take(3)
                .Select(f => new FilterPlanRow { FilterName = f.Name, Count = 5, ExposureSeconds = 300 })
                .ToList();
        }

        private void AddRow() {
            var used = new HashSet<string>(Rows.Select(r => r.FilterName), StringComparer.OrdinalIgnoreCase);
            var next = FilterNames.FirstOrDefault(n => !used.Contains(n)) ?? FilterNames.FirstOrDefault() ?? string.Empty;
            Rows.Add(new FilterPlanRow { FilterName = next });
        }

        private void OnRowsChanged(object sender, NotifyCollectionChangedEventArgs e) {
            foreach (FilterPlanRow row in e.OldItems ?? Array.Empty<FilterPlanRow>()) {
                row.PropertyChanged -= OnRowChanged;
            }
            foreach (FilterPlanRow row in e.NewItems ?? Array.Empty<FilterPlanRow>()) {
                row.PropertyChanged += OnRowChanged;
            }
            RowsEdited();
        }

        private void OnRowChanged(object sender, PropertyChangedEventArgs e) {
            if (FilterPlanRow.IsSaved(e.PropertyName)) {
                RowsEdited();
            }
        }

        private void RowsEdited() {
            if (loading) {
                return;
            }
            // Rows is a [JsonProperty]: this marks the sequence as changed.
            RaisePropertyChanged(nameof(Rows));
            Rebuild();
        }

        private void OnOwnPropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(Status)) {
                RaisePropertyChanged(nameof(IsEditable));
            }
        }

        // -------------------------------------------------------------------
        // values for the whole plan, saved with the sequence
        // -------------------------------------------------------------------

        private int gain = -1;
        private int offset = -1;
        private BinningMode binning = new BinningMode(1, 1);
        private int roundsPerFilter = 1;
        private int stopAfterFailures = DefaultStopAfterFailures;

        public const int DefaultStopAfterFailures = 3;

        /// <summary>Gain of every exposure; -1 leaves it to the camera.</summary>
        [JsonProperty]
        public int Gain {
            get => gain;
            set => SetPlanValue(ref gain, value >= -1 ? value : gain);
        }

        /// <summary>Offset of every exposure; -1 leaves it to the camera.</summary>
        [JsonProperty]
        public int Offset {
            get => offset;
            set => SetPlanValue(ref offset, value >= -1 ? value : offset);
        }

        [JsonProperty]
        public BinningMode Binning {
            get => binning;
            set => SetPlanValue(ref binning, value != null && value.X >= 1 && value.Y >= 1 ? value : binning);
        }

        /// <summary>
        /// How often a filter's block runs before the next filter: 1 is the round robin,
        /// more saves filter changes (and the autofocus runs after them).
        /// </summary>
        [JsonProperty]
        public int RoundsPerFilter {
            get => roundsPerFilter;
            set => SetPlanValue(ref roundsPerFilter, value >= 1 ? value : roundsPerFilter);
        }

        /// <summary>
        /// The plan ends after this many failed exposures in a row (0 = never). Without it a
        /// plan whose exposures all fail (camera gone, image folder missing) would change
        /// filters and run autofocus all night without taking a single image.
        /// </summary>
        [JsonProperty]
        public int StopAfterFailures {
            get => stopAfterFailures;
            set {
                // Not part of the children: no rebuild.
                var kept = value >= 0 ? value : stopAfterFailures;
                if (stopAfterFailures != kept) {
                    stopAfterFailures = kept;
                    if (!loading) {
                        RaisePropertyChanged();
                        RaisePropertyChanged(nameof(Outline));
                    }
                } else if (!loading) {
                    RaisePropertyChanged();
                }
            }
        }

        public IReadOnlyList<BinningMode> BinningModes {
            get {
                var modes = cameraMediator?.GetInfo()?.BinningModes?.ToList();
                if (modes == null || modes.Count == 0) {
                    modes = Enumerable.Range(1, 4).Select(i => new BinningMode((short)i, (short)i)).ToList();
                }
                if (!modes.Contains(binning)) {
                    modes.Insert(0, binning);
                }
                return modes;
            }
        }

        private void SetPlanValue<T>(ref T field, T value, [CallerMemberName] string name = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) {
                // A text box with an invalid entry shows the kept value again.
                if (!loading) {
                    RaisePropertyChanged(name);
                }
                return;
            }
            field = value;
            if (loading) {
                return;
            }
            // A [JsonProperty]: this marks the sequence as changed.
            RaisePropertyChanged(name);
            Rebuild();
        }

        // -------------------------------------------------------------------
        // what the user sees
        // -------------------------------------------------------------------

        /// <summary>What the rows turn into, for the sequencer.</summary>
        public string Outline {
            get {
                var lines = new List<string> { Text.T("Plan_OutlineWait") };
                lines.AddRange(RunOrder().Select((r, i) => (r.Priority
                        ? Text.T("Plan_OutlineRowPriority", i + 2, r.Label)
                        : roundsPerFilter == 1
                            ? Text.T("Plan_OutlineRowOnce", i + 2, r.Label)
                            : Text.T("Plan_OutlineRowRepeat", i + 2, r.Label, roundsPerFilter))
                    + (r.Limits.IsNone ? string.Empty : " · " + r.LimitsText)));
                lines.Add(SettingsLine());
                lines.Add(Text.T("Plan_OutlineEnd"));
                if (stopAfterFailures > 0) {
                    lines.Add(Text.T("Plan_OutlineStop", stopAfterFailures));
                }
                return string.Join(Environment.NewLine, lines);
            }
        }

        /// <summary>"Camera gain and offset, binning 1x1." rather than "gain camera default, offset camera default".</summary>
        private string SettingsLine() {
            if (gain == -1 && offset == -1) {
                return Text.T("Plan_SettingsCamera", binning.Name);
            }
            if (offset == -1) {
                return Text.T("Plan_SettingsCameraOffset", gain, binning.Name);
            }
            if (gain == -1) {
                return Text.T("Plan_SettingsCameraGain", offset, binning.Name);
            }
            return Text.T("Plan_OutlineSettings", gain, offset, binning.Name);
        }

        /// <summary>The rows in one line, for the mini sequencer of the imaging tab.</summary>
        public string RowsText => string.Join(" · ", Rows.Select(r => r.Label));

        private NstnWaitForWindow waiter;
        private IReadOnlyList<NstnWindowCondition> windows = Array.Empty<NstnWindowCondition>();

        /// <summary>The hidden waiting instruction; its night plan is shown in the plan's view.</summary>
        public NstnWaitForWindow Waiter => waiter;

        /// <summary>The NSTN condition of each row's block, for its window tonight and its state now.</summary>
        public IReadOnlyList<NstnWindowCondition> Windows => windows;

        /// <summary>After the children were built or loaded: what the view shows from them.</summary>
        private void ChildrenChanged() {
            // Read N.I.N.A.'s containers here, never under a lock of our own (deadlock rule).
            var items = GetItemsSnapshot();
            waiter = items.OfType<NstnWaitForWindow>().FirstOrDefault();
            windows = items.OfType<ISequenceContainer>()
                .Select(block => (block as IConditionable)?.GetConditionsSnapshot().OfType<NstnWindowCondition>().FirstOrDefault())
                .Where(condition => condition != null)
                .ToList();
            RaisePropertyChanged(nameof(Waiter));
            RaisePropertyChanged(nameof(Windows));
            RaisePropertyChanged(nameof(RowsText));

            // The waiting instruction planned when it was added, before the blocks after it;
            // Night Info shows the bands of the target. Both refresh only every 20 s on their own.
            waiter?.RefreshPlanSoon();
            var target = Parent == null ? null : SequenceTree.TargetOf(Parent);
            foreach (var info in target?.GetItemsSnapshot().OfType<NstnNightInfo>() ?? Enumerable.Empty<NstnNightInfo>()) {
                info.RecalculateSoon();
            }
        }

        // -------------------------------------------------------------------
        // the instructions built from the rows
        // -------------------------------------------------------------------

        private IEnumerable<FilterInfo> ProfileFilters() =>
            (profileService?.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters ?? Enumerable.Empty<FilterInfo>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Name));

        /// <summary>The profile's filter of that name; null if it has none (Switch Filter then reports it).</summary>
        private FilterInfo ResolveFilter(string name) =>
            ProfileFilters().FirstOrDefault(f => string.Equals(f.Name.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

        private static readonly char[] LrgbChannels = { 'L', 'R', 'G', 'B' };

        /// <summary>
        /// The profile's filters of an L/RGB set, found by name ("L", "Lum", "Red", "Baader B" …),
        /// in the order L, R, G, B; null where the profile has none. A filter the NSTN options
        /// assign to a narrowband line is never taken.
        /// </summary>
        private FilterInfo[] LrgbFilters() {
            var narrowband = profileService == null
                ? new HashSet<string>()
                : new HashSet<string>(new NstnOptions(profileService).Assignments.Where(a => !a.IsBroadband).Select(a => a.NinaName.Trim()),
                    StringComparer.OrdinalIgnoreCase);
            return LrgbChannels
                .Select(channel => ProfileFilters().FirstOrDefault(f => !narrowband.Contains(f.Name.Trim()) && FilterAssignments.LrgbChannel(f.Name) == channel))
                .ToArray();
        }

        private void Rebuild() {
            if (Status == SequenceEntityStatus.RUNNING) {
                Logger.Warning($"NSTN filter plan '{Name}': rows changed while running, instructions not rebuilt");
                return;
            }
            foreach (var item in GetItemsSnapshot()) {
                Remove(item);
            }
            foreach (var condition in GetConditionsSnapshot()) {
                Remove(condition);
            }
            foreach (var trigger in GetTriggersSnapshot()) {
                Remove(trigger);
            }

            Add(new NstnWaitForWindow(profileService) { Name = Text.T("Name_Wait") });
            foreach (var row in RunOrder()) {
                Add(BuildBlock(row));
            }
            Add(new NstnNightLeftCondition { Name = Text.T("Name_NightLeft") });

            WatchExposures();
            ChildrenChanged();
            RaisePropertyChanged(nameof(Outline));
            Validate();
        }

        /// <summary>
        /// The rows in the order their blocks run: rows with priority first, each keeping
        /// its window while it is open; then the round robin. Otherwise as in the table.
        /// </summary>
        private IEnumerable<FilterPlanRow> RunOrder() => Rows.Where(r => r.Priority).Concat(Rows.Where(r => !r.Priority));

        private SequentialContainer BuildBlock(FilterPlanRow row) {
            var block = new SequentialContainer { Name = row.Label };
            NstnWindowCondition window;
            if (row.IsLrgb) {
                // One block for the set: L, R, G, B one after the other in the ratio, all
                // judged by the broadband window - a block filter cannot name four filters.
                var filters = LrgbFilters();
                var parts = row.RatioParts;
                for (var i = 0; i < LrgbChannels.Length; i++) {
                    if (parts[i] > 0) {
                        block.Add(new SwitchFilter(profileService, filterWheelMediator) { Filter = filters[i] });
                        block.Add(Series(parts[i], i == 0 ? row.ExposureSeconds : row.RgbExposureSeconds));
                    }
                }
                window = new NstnWindowCondition(profileService) {
                    FilterPreset = string.Empty,
                    Line = Bands.Broadband,
                    BandwidthNm = NightModel.VWidthNm,
                };
            } else {
                block.Add(new SwitchFilter(profileService, filterWheelMediator) { Filter = ResolveFilter(row.FilterName) });
                block.Add(Series(row.Count, row.ExposureSeconds));
                window = new NstnWindowCondition(profileService) { FilterPreset = NstnWindowCondition.BlockFilter };
            }
            window.Name = Text.T("Name_Condition");
            window.ThresholdOffset = row.ThresholdOffset;
            window.MinAltitude = row.MinAltitude;
            window.NotBefore = row.NotBefore;
            window.NotAfter = row.NotAfter;
            block.Add(window);
            // Without an iteration limit a block keeps its window while it is open: priority.
            if (!row.Priority) {
                block.Add(new LoopCondition { Iterations = roundsPerFilter });
            }
            return block;
        }

        /// <summary>A series of one filter: Take Exposure with the plan's values, repeated.</summary>
        private SequentialContainer Series(int count, double exposureSeconds) {
            var series = new SequentialContainer { Name = $"{count} × {exposureSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s" };
            series.Add(new TakeExposure(profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM) {
                ExposureTime = exposureSeconds,
                Gain = gain,
                Offset = offset,
                Binning = new BinningMode(binning.X, binning.Y),
            });
            series.Add(new LoopCondition { Iterations = count });
            return series;
        }

        /// <summary>
        /// Whether a block must give way now: an open priority block above it takes over
        /// after the running exposure (the block's NSTN condition asks before every
        /// exposure). Priority blocks come first, so of two the upper one wins. A block
        /// without an iteration limit is a priority block (see <see cref="BuildBlock"/>).
        /// </summary>
        internal bool MustYield(ISequenceContainer block, double nowMs) {
            foreach (var other in GetItemsSnapshot().OfType<ISequenceContainer>()) {
                if (ReferenceEquals(other, block)) {
                    return false;
                }
                var conditions = (other as IConditionable)?.GetConditionsSnapshot();
                if (conditions == null || conditions.Any(c => c is LoopCondition)) {
                    continue;
                }
                var window = conditions.OfType<NstnWindowCondition>().FirstOrDefault();
                if (window?.CurrentForecast()?.IsOpenAt(nowMs) == true) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The structure the rows ask for and the one the children have, as text; equal
        /// when the children are exactly what <see cref="Rebuild"/> makes of the rows.
        /// </summary>
        public string ExpectedStructure =>
            "Wait" + string.Concat(RunOrder().Select(r =>
                $" | {ExpectedSeries(r)}" + Settings(gain, offset, binning, r.Priority ? null : roundsPerFilter) + Window(r.Limits))) + " | End";

        /// <summary>"Ha 5x300"; an L/RGB set "LRGB L 3x60 R 1x120 G 1x120 B 1x120".</summary>
        private string ExpectedSeries(FilterPlanRow row) {
            if (!row.IsLrgb) {
                return $"{ResolveFilter(row.FilterName)?.Name ?? "-"} {row.Count}x{row.ExposureSeconds.ToString("R", CultureInfo.InvariantCulture)}";
            }
            var filters = LrgbFilters();
            var parts = row.RatioParts;
            return "LRGB" + string.Concat(Enumerable.Range(0, LrgbChannels.Length).Where(i => parts[i] > 0).Select(i =>
                $" {filters[i]?.Name ?? "-"} {parts[i]}x{(i == 0 ? row.ExposureSeconds : row.RgbExposureSeconds).ToString("R", CultureInfo.InvariantCulture)}"));
        }

        /// <summary>"r-" for a block without an iteration limit (priority).</summary>
        private static string Settings(int gain, int offset, BinningMode binning, int? rounds) =>
            FormattableString.Invariant($" g{gain} o{offset} {binning?.Name} r{(rounds is int r ? r.ToString(CultureInfo.InvariantCulture) : "-")}");

        private static string Window(WindowLimits limits) => limits.IsNone ? string.Empty
            : FormattableString.Invariant($" w{limits.ThresholdOffset:+0.##;-0.##;0}/{limits.MinAltitudeDeg?.ToString("0.#", CultureInfo.InvariantCulture) ?? "-"}/{limits.NotBefore?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "-"}-{limits.NotAfter?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "-"}");

        public string ActualStructure {
            get {
                var items = GetItemsSnapshot().ToList();
                if (items.Count == 0 || items[0] is not NstnWaitForWindow) {
                    return "?no wait";
                }
                var text = new StringBuilder("Wait");
                foreach (var item in items.Skip(1)) {
                    if (item.GetType() != typeof(SequentialContainer)) {
                        return $"?{item.GetType().Name}";
                    }
                    var block = (SequentialContainer)item;
                    var inner = block.GetItemsSnapshot().ToList();
                    var gates = block.GetConditionsSnapshot().ToList();
                    // One turn per round with "Loop For Iterations", priority without it.
                    if (gates.Count is < 1 or > 2 || gates[0] is not NstnWindowCondition window
                            || (gates.Count == 2 && gates[1] is not LoopCondition) || block.GetTriggersSnapshot().Count != 0) {
                        return "?block conditions";
                    }
                    // A single filter follows its block's filter; an L/RGB set is judged by the broadband window.
                    var isSet = string.IsNullOrEmpty(window.FilterPreset) && window.Line == Bands.Broadband;
                    if (!isSet && window.FilterPreset != NstnWindowCondition.BlockFilter) {
                        return "?block conditions";
                    }
                    if (inner.Count == 0 || inner.Count % 2 != 0 || (!isSet && inner.Count != 2) || (isSet && inner.Count > 8)) {
                        return "?block items";
                    }
                    var rounds = gates.Count == 2 ? (LoopCondition)gates[1] : null;
                    text.Append(isSet ? " | LRGB" : " |");
                    TakeExposure first = null;
                    for (var i = 0; i < inner.Count; i += 2) {
                        if (inner[i] is not SwitchFilter switchFilter || inner[i + 1].GetType() != typeof(SequentialContainer)) {
                            return "?block items";
                        }
                        var series = (SequentialContainer)inner[i + 1];
                        var exposures = series.GetItemsSnapshot().ToList();
                        var loop = series.GetConditionsSnapshot().ToList();
                        if (exposures.Count != 1 || exposures[0] is not TakeExposure exposure || loop.Count != 1 || loop[0] is not LoopCondition count) {
                            return "?series";
                        }
                        first ??= exposure;
                        if (exposure.Gain != first.Gain || exposure.Offset != first.Offset || !Equals(exposure.Binning, first.Binning)) {
                            return "?series settings";
                        }
                        text.Append($" {switchFilter.Filter?.Name ?? "-"} {count.Iterations}x{exposure.ExposureTime.ToString("R", CultureInfo.InvariantCulture)}");
                    }
                    text.Append(Settings(first.Gain, first.Offset, first.Binning, rounds?.Iterations));
                    text.Append(Window(window.Limits));
                }
                var own = GetConditionsSnapshot().ToList();
                if (own.Count != 1 || own[0] is not NstnNightLeftCondition || GetTriggersSnapshot().Count != 0) {
                    return "?end";
                }
                return text.Append(" | End").ToString();
            }
        }

        // -------------------------------------------------------------------
        // failed exposures in a row
        // -------------------------------------------------------------------

        /// <summary>True once <see cref="StopAfterFailures"/> exposures in a row have failed; read by the end condition.</summary>
        internal bool TooManyFailures => stopAfterFailures > 0 && failedInARow >= stopAfterFailures;

        internal int FailedInARow => failedInARow;

        /// <summary>A new run of the plan starts counting again (called by the end condition).</summary>
        internal void ResetFailures() => failedInARow = 0;

        private void WatchExposures() {
            foreach (var exposure in watchedExposures.Keys) {
                exposure.PropertyChanged -= OnExposureChanged;
            }
            watchedExposures.Clear();
            foreach (var block in GetItemsSnapshot().OfType<ISequenceContainer>()) {
                foreach (var series in block.GetItemsSnapshot().OfType<ISequenceContainer>()) {
                    foreach (var exposure in series.GetItemsSnapshot().OfType<TakeExposure>()) {
                        exposure.PropertyChanged += OnExposureChanged;
                        watchedExposures[exposure] = exposure.Status;
                    }
                }
            }
        }

        private void OnExposureChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(Status) || sender is not TakeExposure exposure || !watchedExposures.TryGetValue(exposure, out var before)) {
                return;
            }
            var status = exposure.Status;
            watchedExposures[exposure] = status;
            // N.I.N.A. 3.2 sets FAILED twice when an exposure fails its validation
            // (RunErrorBehavior, then the catch of the validation exception): one failure.
            if (status == before) {
                return;
            }
            switch (status) {
                case SequenceEntityStatus.FINISHED:
                    failedInARow = 0;
                    break;

                case SequenceEntityStatus.FAILED:
                    failedInARow++;
                    break;
            }
        }

        // -------------------------------------------------------------------
        // loading: factory template → Clone() → these two → N.I.N.A.'s Populate
        // -------------------------------------------------------------------

        [OnDeserializing]
        public void OnPlanDeserializing(StreamingContext context) {
            // The clone of the factory template brings its own rows and children; the
            // file's are added to the same collections (Smart Exposure does the same).
            loading = true;
            foreach (var row in Rows) {
                row.PropertyChanged -= OnRowChanged;
            }
            Rows.Clear();
            Items.Clear();
            Conditions.Clear();
            Triggers.Clear();
        }

        [OnDeserialized]
        public void OnPlanDeserialized(StreamingContext context) {
            loading = false;
            var expected = ExpectedStructure;
            var actual = ActualStructure;
            if (expected == actual) {
                Logger.Info($"NSTN filter plan '{Name}' loaded: {Rows.Count} rows, instructions match ({actual})");
                WatchExposures();
                ChildrenChanged();
            } else {
                Logger.Warning($"NSTN filter plan '{Name}' loaded: instructions ({actual}) do not match the rows ({expected}), rebuilt from the rows");
                Rebuild();
            }
        }

        // -------------------------------------------------------------------
        // validation
        // -------------------------------------------------------------------

        /// <summary>
        /// N.I.N.A. does not look into an immutable container (Sequencer.Validate skips
        /// its children), so the plan validates its instructions itself and reports
        /// their issues as its own.
        /// </summary>
        public override bool Validate() {
            var issues = new List<string>();
            if (Rows.Count == 0) {
                issues.Add(Text.T("Plan_IssueNoRows"));
            }
            foreach (var row in Rows) {
                if (row.IsLrgb) {
                    var filters = LrgbFilters();
                    var parts = row.RatioParts;
                    for (var i = 0; i < LrgbChannels.Length; i++) {
                        if (parts[i] > 0 && filters[i] == null) {
                            issues.Add(LrgbFilterMissing(LrgbChannels[i]));
                        }
                    }
                } else if (row.FilterName.Length == 0) {
                    issues.Add(Text.T("Plan_IssueNoFilter"));
                } else if (ResolveFilter(row.FilterName) == null) {
                    issues.Add(Text.T("Plan_IssueUnknownFilter", row.FilterName));
                }
            }
            if (ExpectedStructure != ActualStructure) {
                issues.Add(Text.T("Plan_IssueMismatch"));
            }
            CollectIssues(this, issues, 0);
            (GetConditionsSnapshot().FirstOrDefault() as NstnNightLeftCondition)?.Preview();

            var distinct = issues.Distinct().ToList();
            Issues = distinct;
            RaisePropertyChanged(nameof(Issues));
            return distinct.Count == 0;
        }

        /// <summary>Which names NSTN looks for, so the user knows what to call the filter.</summary>
        private static string LrgbFilterMissing(char channel) => channel switch {
            'L' => Text.T("Plan_IssueLrgbFilter", "L", "Lum, Luminance, Clear"),
            'R' => Text.T("Plan_IssueLrgbFilter", "R", "Red, Rot"),
            'G' => Text.T("Plan_IssueLrgbFilter", "G", "Green, Grün"),
            _ => Text.T("Plan_IssueLrgbFilter", "B", "Blue, Blau"),
        };

        private static void CollectIssues(ISequenceContainer container, List<string> issues, int depth) {
            if (depth > 8) {
                return;
            }
            if (container is IConditionable conditionable) {
                foreach (var condition in conditionable.GetConditionsSnapshot()) {
                    if (condition.Status != SequenceEntityStatus.DISABLED && condition is IValidatable v) {
                        v.Validate();
                        issues.AddRange(v.Issues);
                    }
                }
            }
            if (container is ITriggerable triggerable) {
                foreach (var trigger in triggerable.GetTriggersSnapshot()) {
                    if (trigger.Status != SequenceEntityStatus.DISABLED && trigger is IValidatable v) {
                        v.Validate();
                        issues.AddRange(v.Issues);
                    }
                }
            }
            foreach (var item in container.GetItemsSnapshot()) {
                if (item.Status == SequenceEntityStatus.DISABLED) {
                    continue;
                }
                if (item is ISequenceContainer child) {
                    CollectIssues(child, issues, depth + 1);
                } else if (item is IValidatable v) {
                    v.Validate();
                    issues.AddRange(v.Issues);
                }
            }
        }

        public override string ToString() {
            return $"Category: {Category}, Container: {nameof(NstnFilterPlan)}, Rows: {string.Join(", ", Rows.Select(r => r.Label))}";
        }
    }
}
