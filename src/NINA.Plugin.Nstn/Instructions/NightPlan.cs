using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NINA.Core.Enum;
using NINA.Plugin.Nstn.Conditions;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.SequenceItem;
using Nstn.Core;

namespace NINA.Plugin.Nstn.Instructions {

    /// <summary>A bar on a timeline row, in pixels from the left of the track.</summary>
    public sealed record PlanBar(double Left, double Width);

    /// <summary>One block of the plan: its window, when it would expose, and how much.</summary>
    public sealed record PlanRow(
        string Label,
        string Expected,
        IReadOnlyList<PlanBar> Windows,
        IReadOnlyList<PlanBar> Exposing,
        double NowLeft,
        bool NowVisible);

    /// <summary>What the night plan shows under "Wait for NSTN window".</summary>
    public sealed record NightPlan(
        bool HasPlan,
        string Summary,
        string AxisStart,
        string AxisEnd,
        IReadOnlyList<PlanRow> Rows,
        string Notes) {

        public static readonly NightPlan Empty = new NightPlan(false, string.Empty, string.Empty, string.Empty, Array.Empty<PlanRow>(), string.Empty);

        /// <summary>Equal plans need no UI update; records compare lists by reference, so compare the text.</summary>
        public string Signature =>
            string.Join("|", new[] { Summary, AxisStart, AxisEnd, Notes }
                .Concat(Rows.Select(r => r.Label + r.Expected + r.NowLeft.ToString("0")
                    + string.Concat(r.Windows.Concat(r.Exposing).Select(b => $"{b.Left:0},{b.Width:0};")))));
    }

    /// <summary>
    /// Reads the blocks next to a "Wait for NSTN window" out of the sequence, plays
    /// the night through with <see cref="PlanSimulator"/> and lays it out for the
    /// sequencer. The sequence is only read, never changed.
    /// </summary>
    public static class NightPlanBuilder {

        /// <summary>Width of a timeline track in the sequencer, in pixels.</summary>
        public const double TrackWidth = 320;

        // A pass through nested sets is expanded into single exposures; cap it so a
        // pathological "Loop 100000" inside a block cannot stall the UI.
        private const int MaxPassExposures = 5000;

        public static NightPlan Build(ISequenceContainer parent, NstnOptions options, DateTime utcNow) {
            if (parent == null) {
                return NightPlan.Empty;
            }
            var now = NightModel.ToEpochMs(utcNow);
            var notes = new List<string>();
            var blocks = new List<PlanBlock>();

            foreach (var item in parent.GetItemsSnapshot()) {
                if (item.Status == SequenceEntityStatus.DISABLED || item is not ISequenceContainer container) {
                    continue;
                }
                var conditions = Conditions(container);
                var nstn = conditions.OfType<NstnWindowCondition>().FirstOrDefault();
                if (nstn == null) {
                    if (Expand(container, 0).Count > 0) {
                        notes.Add(Text.T("Plan_NoCondition", Text.Quote(container.Name)));
                    }
                    continue;
                }
                var forecast = nstn.CurrentForecast();
                if (forecast == null) {
                    notes.Add(Text.T("Plan_BlockIssues", Text.Quote(container.Name), string.Join(" ", nstn.Issues)));
                    continue;
                }
                int? maxPasses = null;
                foreach (var condition in conditions) {
                    switch (condition) {
                        case NstnWindowCondition:
                            break;
                        case LoopCondition loop:
                            maxPasses = maxPasses == null ? loop.Iterations : Math.Min(maxPasses.Value, loop.Iterations);
                            break;
                        default:
                            notes.Add(Text.T("Plan_ConditionNotEstimated", Text.Quote(container.Name), Text.Quote(condition.Name)));
                            break;
                    }
                }
                blocks.Add(new PlanBlock(string.IsNullOrWhiteSpace(container.Name) ? nstn.ShortLabel : container.Name,
                    forecast, Expand(container, 0), maxPasses));
            }

            if (blocks.Count == 0) {
                return NightPlan.Empty;
            }

            // The loop around the blocks decides when the plan ends.
            int? outerPasses = null;
            double? endMs = null;
            var outer = Conditions(parent);
            foreach (var condition in outer) {
                switch (condition) {
                    case LoopCondition loop:
                        var remaining = Math.Max(0, loop.Iterations - loop.CompletedIterations);
                        outerPasses = outerPasses == null ? remaining : Math.Min(outerPasses.Value, remaining);
                        break;
                    case TimeCondition time:
                        var end = now + time.RemainingTime.TotalMilliseconds;
                        endMs = endMs == null ? end : Math.Min(endMs.Value, end);
                        break;
                    default:
                        notes.Add(Text.T("Plan_LoopNotEstimated", Text.Quote(condition.Name)));
                        break;
                }
            }
            if (outer.Count == 0) {
                outerPasses = 1;
                notes.Add(Text.T("Plan_RunsOnce", Text.Quote(parent.Name)));
            }

            // Several blocks and one of them without an iteration limit: that block keeps
            // its window - worth saying, it is the opposite of what a round robin intends.
            if (blocks.Count > 1) {
                foreach (var block in blocks.Where(b => b.MaxPasses == null)) {
                    notes.Add(Text.T("Plan_Priority", Text.Quote(block.Label)));
                }
            }

            var overhead = options.OverheadSeconds;
            var result = PlanSimulator.Simulate(blocks, now, new PlanLimits(overhead, outerPasses, endMs));
            notes.Add(Text.T("Plan_Overhead", overhead));

            return Layout(result, now, notes);
        }

        private static NightPlan Layout(PlanResult result, double now, List<string> notes) {
            var segments = result.Blocks.SelectMany(y => y.Block.Forecast.Segments).ToList();
            var axisStart = segments.Count > 0 ? segments.Min(s => s.Start) : now;
            var axisEnd = segments.Count > 0 ? segments.Max(s => s.End) : now;
            axisEnd = Math.Max(axisEnd, result.EndMs);
            var span = Math.Max(axisEnd - axisStart, 60_000.0);

            double X(double t) => Math.Clamp((t - axisStart) / span, 0, 1) * TrackWidth;
            IReadOnlyList<PlanBar> Bars(IEnumerable<Interval> intervals) =>
                intervals.Select(i => new PlanBar(X(i.Start), Math.Max(1.0, X(i.End) - X(i.Start)))).ToList();

            var nowVisible = now >= axisStart && now <= axisEnd;
            var rows = result.Blocks.Select(y => new PlanRow(
                y.Block.Label,
                Expected(y),
                Bars(y.Block.Forecast.Segments),
                Bars(y.Exposing),
                X(now),
                nowVisible)).ToList();

            var byBand = result.Blocks
                .GroupBy(y => BandName(y.Block.Forecast.Band))
                .Select(g => $"{g.Key} {Duration(g.Sum(y => y.IntegrationSeconds))}");
            var summary = Text.T("Plan_Summary", Clock(Math.Max(now, result.StartMs)), string.Join(", ", byBand), Clock(result.EndMs), Reason(result.Stop));

            return new NightPlan(true, summary, Clock(axisStart), Clock(axisEnd), rows, string.Join(Environment.NewLine, notes.Distinct()));
        }

        private static List<ISequenceCondition> Conditions(ISequenceContainer container) =>
            (container as IConditionable)?.GetConditionsSnapshot()?
                .Where(c => c.Status != SequenceEntityStatus.DISABLED)
                .ToList() ?? new List<ISequenceCondition>();

        /// <summary>The exposure times of one pass through a set, nested sets repeated by their iteration limit.</summary>
        private static List<double> Expand(ISequenceContainer container, int depth) {
            var result = new List<double>();
            if (depth > 8) {
                return result;
            }
            foreach (var item in container.GetItemsSnapshot()) {
                if (item.Status == SequenceEntityStatus.DISABLED) {
                    continue;
                }
                if (item is IExposureItem exposure) {
                    result.Add(exposure.ExposureTime);
                } else if (item is ISequenceContainer child) {
                    var inner = Expand(child, depth + 1);
                    var loop = Conditions(child).OfType<LoopCondition>().FirstOrDefault();
                    var repeat = loop == null ? 1 : Math.Max(0, loop.Iterations);
                    for (var r = 0; r < repeat && result.Count < MaxPassExposures; r++) {
                        result.AddRange(inner);
                    }
                }
                if (result.Count >= MaxPassExposures) {
                    break;
                }
            }
            return result;
        }

        private static string Expected(BlockYield y) {
            if (y.Exposures == 0) {
                return Text.T("Plan_Nothing");
            }
            var lengths = y.Block.PassExposures.Distinct().ToList();
            return lengths.Count == 1
                ? Text.T("Plan_Each", Duration(y.IntegrationSeconds), y.Exposures, lengths[0])
                : Text.T("Plan_Mixed", Duration(y.IntegrationSeconds), y.Exposures);
        }

        /// <summary>The band as the condition shows it: "O III 7 nm", "L/RGB".</summary>
        private static string BandName(EvalFilter band) =>
            BandNames.WithBandwidth(band.Broadband ? BandNames.Broadband : band.Key, band.BandwidthNm);

        private static string Reason(PlanStop stop) => stop switch {
            PlanStop.NothingOpensAgain => Text.T("Plan_StopNothingOpens"),
            PlanStop.OuterPassesDone => Text.T("Plan_StopPasses"),
            PlanStop.EndReached => Text.T("Plan_StopEndTime"),
            PlanStop.NoProgress => Text.T("Plan_StopNoProgress"),
            PlanStop.ExposureLimit => Text.T("Plan_StopLimit", PlanSimulator.MaxExposures),
            _ => stop.ToString(),
        };

        private static string Clock(double epochMs) => Text.Clock(NightModel.ToUtcDateTime(epochMs).ToLocalTime());

        private static string Duration(double seconds) =>
            seconds < 60 ? $"{Math.Round(seconds):0} s" : Text.Duration(seconds / 60);
    }
}
