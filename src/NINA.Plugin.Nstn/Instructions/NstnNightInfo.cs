using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Plugin.Nstn.Conditions;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using Nstn.Core;

namespace NINA.Plugin.Nstn.Instructions {

    /// <summary>
    /// The night at a glance for the target it sits in: twilight, moon, the
    /// target's altitude and the moon's distance, the sky towards the target and
    /// tonight's NSTN windows - plus a check of how the target's sequence is built.
    ///
    /// Takes no time when it runs: Execute only writes a summary to the log. All of
    /// it is display, recomputed from the shared night model, so a template works
    /// on any night and nothing is saved with the sequence.
    /// </summary>
    [ExportMetadata("Name", "NSTN Night Info")]
    [ExportMetadata("Description", "Shows the night for this target: twilight, moon phase and altitude, the target's altitude and distance to the moon, the sky towards the target and tonight's NSTN windows. Checks how the target's sequence is built and explains the recommended structure. Takes no time when it runs.")]
    [ExportMetadata("Icon", "WaxingGibbousMoonSVG")]
    [ExportMetadata("Category", "NSTN")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NstnNightInfo : SequenceItem, IValidatable, IFollowsLanguage {

        // Follows edits to the sequence and moves the "now" line. Like the night plan
        // it only runs while the instruction is part of the active sequence.
        private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(20);

        private static readonly EvalFilter[] DefaultBands = {
            NightModel.BroadbandFilter(),
            NightModel.LineFilter("Ha", Bands.DefaultBandwidthNm),
            NightModel.LineFilter("OIII", Bands.DefaultBandwidthNm),
            NightModel.LineFilter("SII", Bands.DefaultBandwidthNm),
        };

        private readonly IProfileService profileService;
        private readonly ConditionWatchdog watchdog;
        private readonly object gate = new object();

        private IList<string> issues = new List<string>();
        private NightChart chart = NightChart.Empty;
        private IReadOnlyList<SequenceFinding> findings = Array.Empty<SequenceFinding>();
        private string bandsNote = string.Empty;

        [ImportingConstructor]
        public NstnNightInfo(IProfileService profileService) {
            this.profileService = profileService;
            watchdog = new ConditionWatchdog(() => { Refresh(); return Task.CompletedTask; }, RefreshEvery);
            RecalculateCommand = new ActionCommand(() => Task.Run(Refresh));
            PluginLanguage.Follow(profileService);
            PluginLanguage.Register(this, () => Text.T("Name_NightInfo"), () => Text.T("Desc_NightInfo"));
        }

        /// <summary>Everything shown is text built in the language of the moment: rebuild it.</summary>
        public void LanguageChanged() {
            if (Parent != null) {
                Task.Run(Refresh);
            }
        }

        private NstnNightInfo(NstnNightInfo cloneMe) : this(cloneMe.profileService) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() => new NstnNightInfo(this);

        public ICommand RecalculateCommand { get; }

        public NightChart Chart {
            get => chart;
            private set {
                chart = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>The sequence check, warnings first.</summary>
        public IReadOnlyList<SequenceFinding> Findings {
            get => findings;
            private set {
                if (findings.SequenceEqual(value)) {
                    return;
                }
                findings = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Warnings));
                RaisePropertyChanged(nameof(Hints));
                RaisePropertyChanged(nameof(CheckSummary));
            }
        }

        public IReadOnlyList<string> Warnings => findings.Where(f => f.Level == FindingLevel.Warning).Select(f => f.Message).ToList();

        public IReadOnlyList<string> Hints => findings.Where(f => f.Level == FindingLevel.Hint).Select(f => f.Message).ToList();

        public string CheckSummary {
            get {
                if (!chart.HasChart && issues.Count > 0) {
                    return string.Empty;
                }
                var warnings = findings.Count(f => f.Level == FindingLevel.Warning);
                var hints = findings.Count - warnings;
                if (findings.Count == 0) {
                    return Text.T("Info_CheckOk");
                }
                return Text.T("Info_CheckCounts",
                    warnings == 1 ? Text.T("Info_Warning1") : Text.T("Info_Warnings", warnings),
                    hints == 1 ? Text.T("Info_Hint1") : Text.T("Info_Hints", hints));
            }
        }

        /// <summary>Which bands the window rows show, and why.</summary>
        public string BandsNote {
            get => bandsNote;
            private set {
                if (bandsNote == value) {
                    return;
                }
                bandsNote = value;
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
                RaisePropertyChanged(nameof(CheckSummary));
            }
        }

        public override void AfterParentChanged() {
            base.AfterParentChanged();
            if (ItemUtility.IsInRootContainer(Parent)) {
                watchdog.Start();
            } else {
                watchdog.Cancel();
            }
            Task.Run(Refresh);
        }

        /// <summary>
        /// N.I.N.A. calls this while holding its container lock (AfterParentChanged), so
        /// it reads nothing but this item's parent chain and the options - no
        /// container contents, no lock of its own.
        /// </summary>
        public bool Validate() {
            var found = new List<string>();
            if (ItemUtility.RetrieveContextCoordinates(Parent) == null) {
                found.Add(Text.T("Info_IssueNoTarget"));
            }
            if (!new NstnOptions(profileService).HasSkyQuality) {
                found.Add(Text.T("Issue_NoSqm"));
            }
            Issues = found;
            return found.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var facts = chart.Facts.Where(f => f.Logged).Select(f => $"{f.Label}: {f.Text}");
            Logger.Info($"{nameof(NstnNightInfo)}: {string.Join(" | ", facts)}");
            return Task.CompletedTask;
        }

        public override TimeSpan GetEstimatedDuration() => TimeSpan.Zero;

        public override string ToString() => $"Category: {Category}, Item: {nameof(NstnNightInfo)}";

        // -------------------------------------------------------------------

        private void Refresh() {
            // Everything read from N.I.N.A.'s containers is read here, before the gate -
            // see NstnWindowCondition.Refresh for the deadlock this avoids.
            SeqNode tree = null;
            var flipAbove = false;
            var conditions = new List<NstnWindowCondition>();
            try {
                var target = SequenceTree.TargetOf(Parent);
                if (target != null) {
                    conditions = SequenceTree.NstnConditions(target);
                    foreach (var condition in conditions) {
                        // Brings their issues and block filters up to date first.
                        condition.CurrentForecast();
                    }
                    tree = SequenceTree.Convert(target);
                    flipAbove = SequenceTree.MeridianFlipAbove(target);
                }
            } catch (Exception ex) {
                Logger.Error(ex);
            }

            lock (gate) {
                Validate();
                if (Issues.Count > 0) {
                    Chart = NightChart.Empty;
                    Findings = Array.Empty<SequenceFinding>();
                    BandsNote = string.Empty;
                    return;
                }
                try {
                    var options = new NstnOptions(profileService);
                    var settings = options.ToWindowSettings();
                    var astrometry = profileService.ActiveProfile.AstrometrySettings;
                    var j2000 = ItemUtility.RetrieveContextCoordinates(Parent).Coordinates.Transform(Epoch.J2000);
                    var bundle = WindowForecaster.Shared.Night(NstnClock.UtcNow, TimeZoneInfo.Local,
                        astrometry.Latitude, astrometry.Longitude, j2000.RADegrees, j2000.Dec, options.SkyQuality);

                    // The bands of the NSTN conditions below the target, else the usual four.
                    var watched = conditions
                        .Select(c => (Band: Bands.ToFilter(c.Line, c.BandwidthNm), Label: SequenceTree.LabelOf(c)))
                        .GroupBy(b => b.Band)
                        .Select(g => g.First())
                        .ToList();
                    if (watched.Count == 0) {
                        watched = DefaultBands.Select(b => (b, BandNames.WithBandwidth(b.Broadband ? BandNames.Broadband : b.Key, b.BandwidthNm))).ToList();
                        BandsNote = Text.T("Info_BandsDefault");
                    } else {
                        BandsNote = string.Empty;
                    }

                    var info = NightInfoBuilder.Build(bundle, watched.Select(w => w.Band), settings);
                    var now = NightModel.ToEpochMs(NstnClock.UtcNow);
                    Chart = NightChartBuilder.Build(info, now, watched.Select(w => w.Label).ToList());

                    if (tree != null) {
                        var context = new SequenceCheckContext(
                            flipAbove,
                            info,
                            SequenceTree.LinesOfFilter(options.Assignments),
                            ms => Text.Clock(NightModel.ToUtcDateTime(ms).ToLocalTime()));
                        Findings = SequenceCheck.Check(tree, context);
                    }
                } catch (Exception ex) {
                    Logger.Error(ex);
                    Chart = NightChart.Empty;
                    Findings = new[] { new SequenceFinding(FindingLevel.Warning, Text.T("Info_Failed", ex.Message)) };
                }
            }
        }

    }
}
