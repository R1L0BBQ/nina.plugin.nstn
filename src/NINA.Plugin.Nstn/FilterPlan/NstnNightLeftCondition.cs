using System;
using System.ComponentModel.Composition;
using System.Linq;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin.Nstn.Instructions;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using Nstn.Core;

namespace NINA.Plugin.Nstn.FilterPlan {

    /// <summary>
    /// Keeps an instruction set running while at least one NSTN block inside it is open
    /// now or opens later tonight - the end of the night without a clock time. The
    /// filter plan uses it as its own condition; it has to be exported so that
    /// N.I.N.A.'s factory can recreate it when a sequence is loaded. Inside a filter
    /// plan it also ends the plan after too many failed exposures in a row.
    ///
    /// Not IValidatable on purpose: N.I.N.A. validates before every check and turns a
    /// failed validation into FAILED plus a failure event (see NstnWindowCondition).
    /// </summary>
    [ExportMetadata("Name", "Loop while an NSTN window opens tonight")]
    [ExportMetadata("Description", "Keeps the instruction set running while at least one NSTN block inside it is open now or opens later tonight. Ends it after the last window of the night, without a fixed time.")]
    [ExportMetadata("Icon", "WaningGibbousMoonSVG")]
    [ExportMetadata("Category", "NSTN")]
    [Export(typeof(ISequenceCondition))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NstnNightLeftCondition : SequenceCondition, IFollowsLanguage {

        private string summary = string.Empty;
        // One log line per change of state, not one per check.
        private bool? lastReported;
        private bool reportedStop;

        [ImportingConstructor]
        public NstnNightLeftCondition(IProfileService profileService) {
            PluginLanguage.Follow(profileService);
            PluginLanguage.Register(this, () => Text.T("Name_NightLeft"), () => Text.T("Desc_NightLeft"));
        }

        /// <summary>For the filter plan, which builds it in code; the language is followed by then.</summary>
        internal NstnNightLeftCondition() {
            PluginLanguage.Register(this, () => Text.T("Name_NightLeft"), () => Text.T("Desc_NightLeft"));
        }

        private NstnNightLeftCondition(NstnNightLeftCondition cloneMe) : this() {
            CopyMetaData(cloneMe);
        }

        public override object Clone() => new NstnNightLeftCondition(this);

        public void LanguageChanged() {
            Preview();
            // Name is an auto-property without change notification in N.I.N.A.
            RaisePropertyChanged(nameof(Name));
        }

        public string Summary {
            get => summary;
            private set {
                if (summary == value) {
                    return;
                }
                summary = value;
                RaisePropertyChanged();
            }
        }

        public override void SequenceBlockInitialize() {
            (Parent as NstnFilterPlan)?.ResetFailures();
            reportedStop = false;
            base.SequenceBlockInitialize();
        }

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) {
            if (Parent is NstnFilterPlan plan && plan.TooManyFailures) {
                if (!reportedStop) {
                    reportedStop = true;
                    Logger.Warning($"NSTN filter plan '{plan.Name}': {plan.FailedInARow} exposures in a row failed, ending the plan");
                    Notification.ShowWarning(Text.T("Plan_Stopped", plan.FailedInARow));
                }
                Summary = Text.T("NightLeft_Stopped", plan.FailedInARow);
                return false;
            }
            var (watched, left) = Preview();
            if (IsActive() && lastReported != left) {
                lastReported = left;
                Logger.Info($"{nameof(NstnNightLeftCondition)}: {(left ? "window left tonight" : "no window left tonight, ending")} ({watched} blocks)");
            }
            return left;
        }

        /// <summary>Update the display without deciding anything; also used by the filter plan's validation.</summary>
        internal (int Watched, bool Left) Preview() {
            var (watched, left, lastClose) = Evaluate(Parent);
            Summary = watched == 0
                ? Text.T("NightLeft_None")
                : left
                    ? (lastClose is double end
                        ? Text.T("NightLeft_Until", Text.Clock(NightModel.ToUtcDateTime(end).ToLocalTime()))
                        : Text.T("NightLeft_While"))
                    : Text.T("NightLeft_Done");
            return (watched, left);
        }

        /// <summary>The NSTN blocks inside, whether one of them is still to come tonight, and when the last closes.</summary>
        internal static (int Watched, bool Left, double? LastClose) Evaluate(ISequenceContainer container) {
            // CurrentForecast reads N.I.N.A.'s containers before its own lock (deadlock rule).
            var forecasts = NstnWaitForWindow.ConditionsBelow(container).Select(c => c.CurrentForecast()).ToList();
            var now = NightModel.ToEpochMs(NstnClock.UtcNow);
            return (forecasts.Count, PlanEnd.AnyWindowLeft(forecasts, now), PlanEnd.LastClose(forecasts));
        }

        public override string ToString() {
            return $"Condition: {nameof(NstnNightLeftCondition)}, {Summary}";
        }
    }
}
