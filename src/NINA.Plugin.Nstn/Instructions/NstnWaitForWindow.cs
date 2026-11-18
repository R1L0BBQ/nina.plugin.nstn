using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Plugin.Nstn.Conditions;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using Nstn.Core;

namespace NINA.Plugin.Nstn.Instructions {

    /// <summary>
    /// Waits until one of the NSTN windows of the blocks next to it opens.
    ///
    /// Needed for the round-robin pattern - an outer loop around one block per
    /// filter, each with "Loop while NSTN window open". When every window is
    /// closed, NINA 3.2 runs such a loop through its skipped blocks without any
    /// pause (the 250 ms pacing only arrived in 3.3): one core at full load and the
    /// sequencer UI churning, which hurts most on exactly the small astro PCs.
    ///
    /// The bands are read from those conditions, so they are set only once.
    /// </summary>
    [ExportMetadata("Name", "Wait for NSTN window")]
    [ExportMetadata("Description", "Waits until one of the NSTN windows of the blocks in this instruction set opens. The bands come from their 'Loop while NSTN window open' conditions. Put it first in a loop around those blocks, so the loop does not spin while every window is closed.")]
    [ExportMetadata("Icon", "ClockSVG")]
    [ExportMetadata("Category", "NSTN")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NstnWaitForWindow : SequenceItem, IValidatable, IFollowsLanguage {

        // Re-evaluate at least this often while waiting: options, target or the
        // night itself (at local noon) may change underneath a long wait.
        private static readonly TimeSpan Recheck = TimeSpan.FromMinutes(1);

        // The night plan follows edits to the blocks with this delay. The watchdog
        // only runs while the instruction is part of the active sequence, never in the
        // template library - one timer per stored copy would be a leak.
        private static readonly TimeSpan PlanRefresh = TimeSpan.FromSeconds(20);

        private readonly IProfileService profileService;
        private readonly ConditionWatchdog planWatchdog;
        private readonly object planGate = new object();

        private IList<string> issues = new List<string>();
        private string waitSummary = string.Empty;
        private bool reportedNothingOpensTonight;
        private NightPlan plan = NightPlan.Empty;

        [ImportingConstructor]
        public NstnWaitForWindow(IProfileService profileService) {
            this.profileService = profileService;
            planWatchdog = new ConditionWatchdog(() => { RefreshPlan(); return Task.CompletedTask; }, PlanRefresh);
            RecalculateCommand = new ActionCommand(() => Task.Run(RefreshPlan));
            PluginLanguage.Follow(profileService);
            PluginLanguage.Register(this, () => Text.T("Name_Wait"), () => Text.T("Desc_Wait"));
        }

        public void LanguageChanged() {
            if (Parent != null) {
                Task.Run(() => {
                    Validate();
                    RefreshPlan();
                });
            }
        }

        private NstnWaitForWindow(NstnWaitForWindow cloneMe) : this(cloneMe.profileService) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() => new NstnWaitForWindow(this);

        // -------------------------------------------------------------------
        // night plan, display only
        // -------------------------------------------------------------------

        public NightPlan Plan {
            get => plan;
            private set {
                plan = value;
                RaisePropertyChanged();
            }
        }

        public ICommand RecalculateCommand { get; }

        private void RefreshPlan() {
            lock (planGate) {
                NightPlan next;
                try {
                    next = NightPlanBuilder.Build(Parent, new NstnOptions(profileService), DateTime.UtcNow);
                } catch (Exception ex) {
                    Logger.Error(ex);
                    next = NightPlan.Empty with { HasPlan = true, Summary = Text.T("Plan_Failed", ex.Message) };
                }
                if (next.Signature != plan.Signature || next.HasPlan != plan.HasPlan) {
                    Plan = next;
                }
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

        /// <summary>What is being watched and when the next window opens.</summary>
        public string WaitSummary {
            get => waitSummary;
            private set {
                if (waitSummary == value) {
                    return;
                }
                waitSummary = value;
                RaisePropertyChanged();
            }
        }

        public override void AfterParentChanged() {
            base.AfterParentChanged();
            if (ItemUtility.IsInRootContainer(Parent)) {
                planWatchdog.Start();
            } else {
                planWatchdog.Cancel();
            }
            Task.Run(() => {
                Validate();
                RefreshPlan();
            });
        }

        public bool Validate() {
            var state = Evaluate();
            Issues = state.Watched == 0
                ? new List<string> { Text.T("Wait_IssueNoConditions") }
                : new List<string>();
            return Issues.Count == 0;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            reportedNothingOpensTonight = false;
            while (true) {
                token.ThrowIfCancellationRequested();
                var state = Evaluate();
                if (state.Watched == 0) {
                    Logger.Warning($"{nameof(NstnWaitForWindow)}: no NSTN condition in the blocks of this instruction set, not waiting");
                    return;
                }
                if (state.AnyOpen) {
                    return;
                }

                TimeSpan wait;
                if (state.NextOpeningMs is double opening) {
                    wait = TimeSpan.FromMilliseconds(Math.Max(1000, opening - Now()));
                } else {
                    // Nothing opens again this night. The enclosing loop's own end
                    // condition (dawn, altitude, ...) interrupts this wait; until then
                    // keep checking, the next night starts at local noon.
                    if (!reportedNothingOpensTonight) {
                        reportedNothingOpensTonight = true;
                        Logger.Info($"{nameof(NstnWaitForWindow)}: no NSTN window opens again tonight, waiting for the enclosing loop to end");
                    }
                    wait = Recheck;
                }
                if (wait > Recheck) {
                    wait = Recheck;
                }
                await CoreUtil.Wait(wait, true, token, progress, state.Status);
            }
        }

        public override TimeSpan GetEstimatedDuration() {
            var state = Evaluate();
            if (state.AnyOpen || state.NextOpeningMs == null) {
                return TimeSpan.Zero;
            }
            return TimeSpan.FromMilliseconds(Math.Max(0, state.NextOpeningMs.Value - Now()));
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(NstnWaitForWindow)}, {WaitSummary}";
        }

        // -------------------------------------------------------------------

        private readonly record struct WaitState(int Watched, bool AnyOpen, double? NextOpeningMs, string Status);

        private WaitState Evaluate() {
            var now = Now();
            var watched = new List<(NstnWindowCondition Condition, BandForecast Forecast)>();
            foreach (var condition in ConditionsBelow(Parent)) {
                watched.Add((condition, condition.CurrentForecast()));
            }

            var usable = watched.Where(w => w.Forecast != null).ToList();
            var open = usable.Where(w => w.Forecast.IsOpenAt(now)).ToList();
            var next = usable
                .Select(w => (w.Condition, Opening: w.Forecast.NextChangeAfter(now)))
                .Where(w => w.Opening != null)
                .OrderBy(w => w.Opening)
                .FirstOrDefault();

            var bands = string.Join(", ", watched.Select(w => w.Condition.ShortLabel).Distinct());
            string status;
            if (watched.Count == 0) {
                status = Text.T("Wait_NoCondition");
            } else if (open.Count > 0) {
                status = Text.T("Wait_OpenNow", string.Join(", ", open.Select(w => w.Condition.ShortLabel).Distinct()));
            } else if (next.Condition != null) {
                status = Text.T("Wait_Opens", next.Condition.ShortLabel, Clock(next.Opening.Value));
            } else {
                status = Text.T("Wait_NothingTonight");
            }
            WaitSummary = watched.Count == 0 ? status : Text.T("Wait_Watches", bands, status);

            return new WaitState(
                Watched: watched.Count,
                AnyOpen: open.Count > 0,
                NextOpeningMs: open.Count > 0 ? null : next.Opening,
                Status: status);
        }

        /// <summary>
        /// The NSTN conditions on the blocks inside this instruction's container,
        /// at any depth. Not the container's own conditions: if the container itself
        /// is closed, its loop ends and there is nothing to wait for.
        /// </summary>
        internal static IEnumerable<NstnWindowCondition> ConditionsBelow(ISequenceContainer container) {
            if (container == null) {
                yield break;
            }
            foreach (var item in container.GetItemsSnapshot()) {
                if (item is not ISequenceContainer child || item.Status == SequenceEntityStatus.DISABLED) {
                    continue;
                }
                if (child is IConditionable conditionable) {
                    foreach (var condition in conditionable.GetConditionsSnapshot().OfType<NstnWindowCondition>()) {
                        if (condition.Status != SequenceEntityStatus.DISABLED) {
                            yield return condition;
                        }
                    }
                }
                foreach (var nested in ConditionsBelow(child)) {
                    yield return nested;
                }
            }
        }

        private static double Now() => NightModel.ToEpochMs(DateTime.UtcNow);

        private static string Clock(double epochMs) => Text.Clock(NightModel.ToUtcDateTime(epochMs).ToLocalTime());
    }
}
