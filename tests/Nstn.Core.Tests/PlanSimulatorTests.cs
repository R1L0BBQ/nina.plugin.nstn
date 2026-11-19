using System;
using System.Linq;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    /// <summary>
    /// The simulation against hand-built windows. Times in seconds from zero; the
    /// expected exposure sequences are worked out by hand from N.I.N.A.'s rules
    /// (conditions before each exposure, a started exposure runs to its end).
    /// </summary>
    public class PlanSimulatorTests {

        private static BandForecast Window(params (double Start, double End)[] seconds) =>
            new BandForecast(new DateOnly(2026, 11, 17), NightModel.LineFilter("Ha", 7),
                seconds.Select(s => new Interval(s.Start * 1000, s.End * 1000)).ToArray(),
                23.1, 20.34, 21.0, new SkyResult(Array.Empty<SkySample>(), 21.0));

        private static PlanBlock Block(string label, BandForecast forecast, int? maxPasses, params double[] exposures) =>
            new PlanBlock(label, forecast, exposures, maxPasses);

        private static readonly PlanLimits NoOverhead = new PlanLimits(OverheadSeconds: 0);

        [Fact]
        public void ABlockWithoutLimitFillsItsWindow() {
            var result = PlanSimulator.Simulate(new[] { Block("Ha", Window((0, 3600)), null, 300) }, 0, NoOverhead);
            Assert.Equal(12, result.Blocks[0].Exposures);
            Assert.Equal(3600, result.Blocks[0].IntegrationSeconds);
            Assert.Equal(PlanStop.NothingOpensAgain, result.Stop);
            Assert.Equal(new[] { new Interval(0, 3_600_000) }, result.Blocks[0].Exposing);
        }

        [Fact]
        public void IterationLimitsMakeARoundRobin() {
            // A open 0-3600, B open 0-7200; one 600 s exposure per pass each.
            // Alternating: A 0, B 600, A 1200, B 1800, A 2400, B 3000;
            // at 3600 A is closed (half-open window), B runs on until 7200.
            var result = PlanSimulator.Simulate(new[] {
                Block("A", Window((0, 3600)), 1, 600),
                Block("B", Window((0, 7200)), 1, 600),
            }, 0, NoOverhead);
            Assert.Equal(3, result.Blocks[0].Exposures);
            Assert.Equal(9, result.Blocks[1].Exposures);
            Assert.Equal(PlanStop.NothingOpensAgain, result.Stop);
        }

        [Fact]
        public void WithoutALimitTheFirstBlockKeepsTheWindow() {
            var result = PlanSimulator.Simulate(new[] {
                Block("A", Window((0, 3600)), null, 600),
                Block("B", Window((0, 3600)), null, 600),
            }, 0, NoOverhead);
            Assert.Equal(6, result.Blocks[0].Exposures);
            Assert.Equal(0, result.Blocks[1].Exposures);
        }

        [Fact]
        public void TheWaitJumpsToTheNextOpening() {
            var result = PlanSimulator.Simulate(new[] { Block("O III", Window((1000, 1600)), null, 300) }, 0, NoOverhead);
            Assert.Equal(1_000_000, result.Blocks[0].Exposing[0].Start);
            Assert.Equal(2, result.Blocks[0].Exposures);
        }

        [Fact]
        public void AStartedExposureRunsPastTheClosingWindow() {
            // 0 and 600 start inside the window; the second ends at 1200, after it closed.
            var result = PlanSimulator.Simulate(new[] { Block("Ha", Window((0, 1000)), null, 600) }, 0, NoOverhead);
            Assert.Equal(2, result.Blocks[0].Exposures);
            Assert.Equal(1_200_000, result.Blocks[0].Exposing[0].End);
        }

        [Fact]
        public void OverheadIsAddedAfterEveryExposure() {
            // 0 -> 310 -> 620: the third would start exactly when the window closes.
            var result = PlanSimulator.Simulate(new[] { Block("Ha", Window((0, 620)), null, 300) },
                0, new PlanLimits(OverheadSeconds: 10));
            Assert.Equal(2, result.Blocks[0].Exposures);
            Assert.Single(result.Blocks[0].Exposing);
        }

        [Fact]
        public void OuterPassesAndEndTimeStopThePlan() {
            var blocks = new[] { Block("Ha", Window((0, 36000)), 1, 300, 300) };
            var byPasses = PlanSimulator.Simulate(blocks, 0, new PlanLimits(0, OuterPasses: 3));
            Assert.Equal(6, byPasses.Blocks[0].Exposures);
            Assert.Equal(PlanStop.OuterPassesDone, byPasses.Stop);

            var byTime = PlanSimulator.Simulate(blocks, 0, new PlanLimits(0, EndMs: 1_000_000));
            Assert.Equal(4, byTime.Blocks[0].Exposures);  // starts 0, 300, 600, 900
            Assert.Equal(PlanStop.EndReached, byTime.Stop);
        }

        [Fact]
        public void APassCutShortByTheWindowEndsTheBlockForThisPass() {
            // Pass = 3 x 300 s, window closes at 500: exposures at 0 and 300 (the
            // second runs to 600), then the condition fails before the third.
            var result = PlanSimulator.Simulate(new[] { Block("O III", Window((0, 500)), null, 300, 300, 300) }, 0, NoOverhead);
            Assert.Equal(2, result.Blocks[0].Exposures);
            Assert.Equal(PlanStop.NothingOpensAgain, result.Stop);
        }

        [Fact]
        public void ABlockWithoutExposuresDoesNotLoopForever() {
            var result = PlanSimulator.Simulate(new[] { Block("empty", Window((0, 3600)), null) }, 0, NoOverhead);
            Assert.Equal(PlanStop.NoProgress, result.Stop);
            Assert.Equal(0, result.Blocks[0].Exposures);
        }

        /// <summary>
        /// Marc's night of 19/20 Nov: the L/RGB window opened in the middle of a round and
        /// closed before the next one, so L/RGB got nothing. A priority row takes over
        /// after the running exposure. Here P (3:1:1:1, L 60 s, RGB 120 s) opens at 2000
        /// while B exposes 1500-1800 and 1800-2100; B stops after that exposure, P starts
        /// at 2100 - not at 3000, when B's set and the round would have ended.
        /// </summary>
        [Fact]
        public void APriorityRowTakesOverAfterTheRunningExposure() {
            var result = PlanSimulator.Simulate(new[] {
                new PlanBlock("L/RGB", Window((2000, 4500)), new double[] { 60, 60, 60, 120, 120, 120 }, null, Preempts: true),
                Block("Ha", Window((0, 10000)), 1, 300, 300, 300, 300, 300),
                Block("S II", Window((0, 10000)), 1, 300, 300, 300, 300, 300),
            }, 0, NoOverhead);
            Assert.Equal(new Interval(1_500_000, 2_100_000), result.Blocks[2].Exposing[0]);
            Assert.Equal(2_100_000, result.Blocks[0].Exposing[0].Start);
            // While its window is open nothing else runs; the last exposure started at 4440 runs to 4560.
            Assert.Equal(new[] { new Interval(2_100_000, 4_560_000) }, result.Blocks[0].Exposing);
            Assert.DoesNotContain(result.Blocks[1].Exposing.Concat(result.Blocks[2].Exposing), i => i.Start < 4_560_000 && i.End > 2_100_000);
            Assert.Equal(28, result.Blocks[0].Exposures);
        }

        /// <summary>Two priority rows open at once: the upper one runs, the lower waits until it closes.</summary>
        [Fact]
        public void OfTwoPriorityRowsTheUpperOneWins() {
            var result = PlanSimulator.Simulate(new[] {
                new PlanBlock("upper", Window((1000, 3000)), new double[] { 300 }, null, Preempts: true),
                new PlanBlock("lower", Window((0, 4000)), new double[] { 300 }, null, Preempts: true),
                Block("rest", Window((0, 5000)), 1, 300),
            }, 0, NoOverhead);
            Assert.Equal(new[] { new Interval(1_200_000, 3_000_000) }, result.Blocks[0].Exposing);
            Assert.Equal(new[] { new Interval(0, 1_200_000), new Interval(3_000_000, 4_200_000) }, result.Blocks[1].Exposing);
            Assert.True(result.Blocks[2].Exposing.All(i => i.Start >= 4_200_000), string.Join(", ", result.Blocks[2].Exposing));
        }

        [Fact]
        public void TheExposureLimitStopsADegeneratePlan() {
            var result = PlanSimulator.Simulate(new[] { Block("tiny", Window((0, 1e9)), null, 0.001) }, 0, NoOverhead);
            Assert.Equal(PlanStop.ExposureLimit, result.Stop);
            Assert.Equal(PlanSimulator.MaxExposures, result.Blocks[0].Exposures);
        }
    }
}
