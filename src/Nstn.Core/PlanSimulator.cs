// What a round-robin night would yield: the instruction sets of an outer loop, each
// gated by its band's NSTN window, played through the night the way N.I.N.A.'s
// sequencer runs them. Answers "does this structure get my hours?" before the night.
//
// Modelled after N.I.N.A. 3.2 (SequentialStrategy):
// * "Wait for NSTN window" first in every pass of the outer loop: if no band is open,
//   time jumps to the next opening; if none opens again, the night is over.
// * The blocks run in order. A block's conditions are checked before each exposure
//   (also inside nested sets); a closed window ends the block for this pass. An
//   exposure that has started always runs to its end.
// * A block repeats while its window is open, up to its own iteration limit. Without
//   a limit it keeps the window to itself - strict priority, not a round robin.
// * A preempting block (priority row of a filter plan) takes over as soon as its window
//   opens: every block below it stops after the running exposure while it is open, the
//   pass runs out, and the next pass starts with it. Of two, the upper one wins.
// Not modelled: autofocus, meridian flip, filter changes, slews, plate solving. A
// flat overhead per exposure stands in for download, saving and dithering.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nstn.Core {

    /// <summary>
    /// One instruction set of the outer loop with "Loop while NSTN window open".
    /// <paramref name="PassExposures"/> are the exposure times of one pass through the
    /// set, in seconds and in order; <paramref name="MaxPasses"/> its iteration limit,
    /// null when it has none. <paramref name="Preempts"/>: the blocks below it yield
    /// while its window is open (a priority row of a filter plan).
    /// </summary>
    public sealed record PlanBlock(string Label, BandForecast Forecast, IReadOnlyList<double> PassExposures, int? MaxPasses, bool Preempts = false);

    /// <param name="OverheadSeconds">Added after every exposure: download, saving, dithering.</param>
    /// <param name="OuterPasses">Remaining passes of the outer loop, null when its end is not a pass count.</param>
    /// <param name="EndMs">End time of the outer loop (e.g. "Loop until time"), null if there is none.</param>
    public sealed record PlanLimits(double OverheadSeconds = 10, int? OuterPasses = null, double? EndMs = null);

    public sealed record BlockYield(PlanBlock Block, int Exposures, double IntegrationSeconds, IReadOnlyList<Interval> Exposing);

    public enum PlanStop {
        /// <summary>No band of the plan opens again tonight.</summary>
        NothingOpensAgain,
        /// <summary>The outer loop's iterations are used up.</summary>
        OuterPassesDone,
        /// <summary>The outer loop's end time is reached.</summary>
        EndReached,
        /// <summary>A pass ran without a single exposure although a window was open.</summary>
        NoProgress,
        /// <summary>Safety stop after <see cref="PlanSimulator.MaxExposures"/> exposures.</summary>
        ExposureLimit,
    }

    public sealed record PlanResult(IReadOnlyList<BlockYield> Blocks, double StartMs, double EndMs, int OuterPasses, PlanStop Stop);

    public static class PlanSimulator {

        public const int MaxExposures = 20_000;

        public static PlanResult Simulate(IReadOnlyList<PlanBlock> blocks, double startMs, PlanLimits limits) {
            var count = new int[blocks.Count];
            var seconds = new double[blocks.Count];
            var exposing = blocks.Select(_ => new List<Interval>()).ToArray();
            var overheadMs = Math.Max(0, limits.OverheadSeconds) * 1000.0;
            var t = startMs;
            var passes = 0;
            var total = 0;
            PlanStop stop;

            bool Open(int i) => blocks[i].Forecast.IsOpenAt(t);
            // Open, and no open preempting block above it.
            bool MayRun(int i) => Open(i) && !Enumerable.Range(0, i).Any(j => blocks[j].Preempts && Open(j));
            bool PastEnd() => limits.EndMs is double end && t >= end;

            while (true) {
                if (limits.OuterPasses is int maxPasses && passes >= maxPasses) {
                    stop = PlanStop.OuterPassesDone;
                    break;
                }
                if (PastEnd()) {
                    stop = PlanStop.EndReached;
                    break;
                }

                // Wait for NSTN window.
                if (!Enumerable.Range(0, blocks.Count).Any(Open)) {
                    double? next = null;
                    foreach (var block in blocks) {
                        var opening = block.Forecast.NextChangeAfter(t);
                        if (opening != null && (next == null || opening < next)) {
                            next = opening;
                        }
                    }
                    if (next == null) {
                        stop = PlanStop.NothingOpensAgain;
                        break;
                    }
                    t = next.Value;
                    if (PastEnd()) {
                        stop = PlanStop.EndReached;
                        break;
                    }
                }

                passes++;
                var progressed = false;
                var limitHit = false;
                for (var i = 0; i < blocks.Count && !limitHit; i++) {
                    var block = blocks[i];
                    for (var pass = 0; block.MaxPasses == null || pass < block.MaxPasses; pass++) {
                        if (!MayRun(i) || PastEnd()) {
                            break;
                        }
                        var ran = 0;
                        foreach (var exposure in block.PassExposures) {
                            if (!MayRun(i) || PastEnd()) {
                                break;
                            }
                            if (total >= MaxExposures) {
                                limitHit = true;
                                break;
                            }
                            exposing[i].Add(new Interval(t, t + exposure * 1000.0));
                            seconds[i] += exposure;
                            count[i]++;
                            total++;
                            ran++;
                            t += exposure * 1000.0 + overheadMs;
                        }
                        if (ran > 0) {
                            progressed = true;
                        }
                        // An empty pass would repeat forever; a cut-short pass means the
                        // window closed, which ends the block for this pass of the loop.
                        if (limitHit || ran < block.PassExposures.Count || ran == 0) {
                            break;
                        }
                    }
                }

                if (limitHit) {
                    stop = PlanStop.ExposureLimit;
                    break;
                }
                if (!progressed) {
                    stop = PlanStop.NoProgress;
                    break;
                }
            }

            var yields = blocks.Select((block, i) => new BlockYield(
                block,
                count[i],
                seconds[i],
                // Back-to-back exposures as one bar: the gap between them is the overhead.
                NightModel.MergeSegments(exposing[i], overheadMs + 1.0))).ToList();
            return new PlanResult(yields, startMs, t, passes, stop);
        }
    }
}
