using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
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
    public class NstnWindowCondition : SequenceCondition, IValidatable {

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
        }

        private NstnWindowCondition(NstnWindowCondition cloneMe) : this(cloneMe.profileService) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new NstnWindowCondition(this) {
                Line = Line,
                BandwidthNm = BandwidthNm,
            };
        }

        [OnDeserialized]
        public void OnDeserialized(StreamingContext context) {
            RunWatchdogIfInsideSequenceRoot();
        }

        // -------------------------------------------------------------------
        // saved with the sequence
        // -------------------------------------------------------------------

        [JsonProperty]
        public string Line {
            get => line;
            set {
                line = Bands.Normalize(value);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsNarrowband));
                RaisePropertyChanged(nameof(ShortLabel));
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
                ScheduleRefresh();
            }
        }

        // -------------------------------------------------------------------
        // display, never saved
        // -------------------------------------------------------------------

        public IReadOnlyList<BandChoice> BandChoices => Bands.All;

        public bool IsNarrowband => Line != Bands.Broadband;

        public string ShortLabel => Bands.ShortLabel(Line);

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
            var open = forecast != null && forecast.IsOpenAt(NightModel.ToEpochMs(DateTime.UtcNow));
            if (IsActive() && lastReportedOpen != open) {
                lastReportedOpen = open;
                var reason = forecast == null ? string.Join(" ", Issues) : NowSummary;
                Logger.Info($"{nameof(NstnWindowCondition)} {ShortLabel} {(open ? "open" : "closed")}: {reason}");
            }
            return open;
        }

        public bool Validate() {
            Refresh(force: false);
            return Issues.Count == 0;
        }

        public override string ToString() {
            return $"Condition: {nameof(NstnWindowCondition)}, Line: {Line}, Bandwidth: {BandwidthNm} nm, {NowSummary}";
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
            lock (refreshGate) {
                if (!force && DateTime.UtcNow - lastRefreshUtc < ReuseFor) {
                    ShowNow(lastForecast);
                    return lastForecast;
                }
                var found = new List<string>();
                BandForecast forecast = null;
                try {
                    var options = new NstnOptions(profileService);
                    var context = ItemUtility.RetrieveContextCoordinates(Parent);
                    if (context == null) {
                        found.Add("Place this condition inside a target container: NSTN needs the target's coordinates.");
                    }
                    if (!options.HasSkyQuality) {
                        found.Add("Set the site's sky quality (SQM) in Options > Plugins > NSTN.");
                    }
                    if (found.Count == 0) {
                        var astrometry = profileService.ActiveProfile.AstrometrySettings;
                        var j2000 = context.Coordinates.Transform(Epoch.J2000);
                        forecast = WindowForecaster.Shared.Forecast(
                            DateTime.UtcNow,
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
                    found.Add($"NSTN calculation failed: {ex.Message}");
                    forecast = null;
                }

                Issues = found;
                ShowForecast(forecast);
                lastForecast = forecast;
                lastRefreshUtc = DateTime.UtcNow;
                return forecast;
            }
        }

        private void ShowForecast(BandForecast forecast) {
            if (forecast == null) {
                WindowSummary = string.Empty;
            } else {
                var night = forecast.NightDate.ToString("d MMM", CultureInfo.InvariantCulture);
                if (forecast.NeverOpensAtSite) {
                    WindowSummary = string.Format(CultureInfo.InvariantCulture,
                        "Never opens at this site: {0} at {1:0.#} nm needs a sky of at least {2:0.00} mag/arcsec², the site has {3:0.00}. Use a narrower filter or lower the narrowband threshold in the plugin options.",
                        ShortLabel, BandwidthNm, forecast.MinimumSkyBrightness, forecast.SqmZenith);
                } else if (forecast.Segments.Count == 0) {
                    WindowSummary = $"Tonight ({night}): never open";
                } else {
                    var spans = string.Join(", ", forecast.Segments.Select(s => $"{Clock(s.Start)}–{Clock(s.End)}"));
                    WindowSummary = $"Tonight ({night}): {spans} · {Duration(forecast.OpenMinutes)}";
                }
            }
            ShowNow(forecast);
        }

        /// <summary>The part of the display that moves with the clock.</summary>
        private void ShowNow(BandForecast forecast) {
            if (forecast == null) {
                NowSummary = "No forecast";
                IsOpenNow = false;
                return;
            }

            var now = NightModel.ToEpochMs(DateTime.UtcNow);
            var open = forecast.IsOpenAt(now);
            var change = forecast.NextChangeAfter(now);
            var state = open
                ? (change == null ? "Open" : $"Open until {Clock(change.Value)}")
                : (change == null ? "Closed for the rest of the night" : $"Closed, opens {Clock(change.Value)}");
            var magnitude = forecast.InBandMagnitudeAt(now);
            NowSummary = magnitude == null
                ? state
                : string.Format(CultureInfo.InvariantCulture, "{0} · sky in band {1:0.00}, needs {2:0.00}", state, magnitude.Value, forecast.Threshold);
            IsOpenNow = open;
        }

        private static string Clock(double epochMs) =>
            NightModel.ToUtcDateTime(epochMs).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        private static string Duration(double minutes) {
            var total = (int)Math.Round(minutes);
            return total >= 60 ? $"{total / 60} h {total % 60:00} min" : $"{total} min";
        }
    }
}
