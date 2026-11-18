using System;
using System.Collections.Generic;
using System.Linq;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    public class SequenceCheckTests {

        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        private static readonly Dictionary<string, string[]> FilterLines = new(StringComparer.OrdinalIgnoreCase) {
            ["Ha"] = new[] { "Ha" },
            ["OIII"] = new[] { "OIII" },
            ["L"] = new[] { "RGB" },
            ["D1"] = new[] { "Ha", "OIII" },
        };

        // --- a small tree builder: children, conditions and triggers in any order

        private static SeqNode Container(string name, params object[] parts) {
            var node = new SeqNode(name, SeqItemKind.Container);
            foreach (var part in parts) {
                switch (part) {
                    case SeqNode child: node.Children.Add(child); break;
                    case SeqCondition condition: node.Conditions.Add(condition); break;
                    case SeqTriggerKind trigger: node.Triggers.Add(trigger); break;
                    default: throw new ArgumentException(part?.ToString());
                }
            }
            return node;
        }

        private static SeqCondition Nstn(string line) => new(SeqConditionKind.NstnWindow, "Loop while NSTN window open") {
            Band = line == "RGB" ? NightModel.BroadbandFilter() : NightModel.LineFilter(line, 7),
            Label = $"{line} 7 nm",
        };

        private static SeqCondition FollowingBlockFilter => new(SeqConditionKind.NstnWindow, "Loop while NSTN window open") {
            Band = NightModel.LineFilter("Ha", 7),
            Label = "from block filter",
            FollowsBlockFilter = true,
        };

        private static SeqCondition Iterations => new(SeqConditionKind.LoopForIterations, "Loop For Iterations");

        private static SeqCondition UntilTime => new(SeqConditionKind.LoopUntilTime, "Loop Until Time");

        private static SeqNode Exposure(string filter) => new("Smart Exposure", SeqItemKind.Exposure) { Filter = filter };

        private static SeqNode Switch(string filter) => new("Switch Filter", SeqItemKind.SwitchFilter) { Filter = filter };

        private static SeqNode Wait => new("Wait for NSTN window", SeqItemKind.NstnWait);

        private static SeqNode Slew => new("Slew and center", SeqItemKind.Other);

        private static SeqNode Block(string name, string line, params object[] extra) =>
            Container(name, new object[] { Nstn(line), Switch(line), Exposure(line) }.Concat(extra).ToArray());

        /// <summary>The pattern the README recommends: target, slew, loop until dawn, wait first, round robin blocks.</summary>
        private static SeqNode Recommended(params object[] targetExtras) =>
            Container("M33", new object[] {
                Slew,
                Container("Loop", UntilTime, Wait,
                    Block("Block OIII", "OIII", Iterations),
                    Block("Block Ha", "Ha", Iterations)),
            }.Concat(targetExtras).ToArray());

        private static NightInfo Night(double sqm = 20.8, double dec = 30.7, string date = "2025-09-18") {
            var day = DateOnly.Parse(date);
            var bundle = new WindowForecaster().Night(new DateTime(day.Year, day.Month, day.Day, 20, 0, 0, DateTimeKind.Utc),
                Berlin, 50, 7, 23.5, dec, sqm);
            return NightInfoBuilder.Build(bundle, new[] { NightModel.LineFilter("Ha", 7), NightModel.LineFilter("OIII", 7) });
        }

        private static IReadOnlyList<SequenceFinding> Check(SeqNode target, NightInfo? night = null, bool flipAbove = false) =>
            SequenceCheck.Check(target, new SequenceCheckContext(
                flipAbove,
                night,
                name => FilterLines.TryGetValue(name, out var lines) ? lines : null,
                ms => NightModel.ToUtcDateTime(ms).ToString("HH:mm")));

        private static void AssertOne(IReadOnlyList<SequenceFinding> findings, FindingLevel level, string fragment) {
            var matching = findings.Where(f => f.Message.Contains(fragment, StringComparison.Ordinal)).ToList();
            Assert.True(matching.Count == 1, $"expected one finding with '{fragment}', got:\n{string.Join("\n", findings.Select(f => f.Message))}");
            Assert.Equal(level, matching[0].Level);
        }

        [Fact]
        public void TheRecommendedPatternPasses() {
            Assert.Empty(Check(Recommended(SeqTriggerKind.MeridianFlip), Night()));
            Assert.Empty(Check(Recommended(), Night(), flipAbove: true));
        }

        [Fact]
        public void ALoopWithoutConditionRunsOnce() {
            var target = Container("M33", Slew, Container("Loop", Block("Block Ha", "Ha", Iterations)));
            var findings = Check(target);
            AssertOne(findings, FindingLevel.Warning, "has no loop condition");
            // The wait only matters in a loop; a single pass needs no brake.
            Assert.DoesNotContain(findings, f => f.Message.Contains("has no 'Wait"));
        }

        [Fact]
        public void ALoopWithoutWaitSpins() {
            var target = Container("M33", Container("Loop", UntilTime, Block("Block Ha", "Ha", Iterations)));
            AssertOne(Check(target), FindingLevel.Warning, "has no 'Wait for NSTN window'");
        }

        [Fact]
        public void TheWaitBelongsFirst() {
            var target = Container("M33", Container("Loop", UntilTime, Block("Block Ha", "Ha", Iterations), Wait));
            AssertOne(Check(target), FindingLevel.Hint, "Move 'Wait for NSTN window' to the top");
        }

        [Fact]
        public void TheWaitDoesNotBelongInsideABlock() {
            var target = Container("M33", Container("Loop", UntilTime, Wait, Block("Block Ha", "Ha", Iterations, Wait)));
            AssertOne(Check(target), FindingLevel.Warning, "sits inside block");
        }

        [Fact]
        public void BlocksWithoutIterationsTakePriorityOnlyWhenThereAreSeveral() {
            var two = Container("M33", Container("Loop", UntilTime, Wait, Block("Block OIII", "OIII"), Block("Block Ha", "Ha")));
            var findings = Check(two);
            Assert.Equal(2, findings.Count(f => f.Message.Contains("has no 'Loop For Iterations'")));

            var one = Container("M33", Container("Loop", UntilTime, Wait, Block("Block Ha", "Ha")));
            Assert.Empty(Check(one));
        }

        [Fact]
        public void ABlockWithoutExposureIsReported() {
            var target = Container("M33", Container("Loop", UntilTime, Wait, Container("Block Ha", Nstn("Ha"), Iterations, Switch("Ha"))));
            AssertOne(Check(target), FindingLevel.Warning, "contains no exposure");
        }

        [Theory]
        [InlineData("OIII", true)]    // H-alpha window decides over the O III filter
        [InlineData("L", true)]
        [InlineData("Ha", false)]
        [InlineData("D1", false)]     // a duo passes H-alpha too
        [InlineData("Unknown", false)] // unassigned: the condition cannot know, and says so itself in block mode
        public void ABlockFilterMustMatchTheWatchedBand(string filter, bool reported) {
            var target = Container("M33", Container("Loop", UntilTime, Wait,
                Container("Block", Nstn("Ha"), Iterations, Switch(filter), Exposure(filter))));
            var findings = Check(target);
            Assert.Equal(reported, findings.Any(f => f.Level == FindingLevel.Warning && f.Message.Contains($"uses filter '{filter}'")));
        }

        [Fact]
        public void FollowingTheBlockFilterIsNeverAMismatch() {
            var target = Container("M33", Container("Loop", UntilTime, Wait,
                Container("Block", FollowingBlockFilter, Iterations, Switch("OIII"), Exposure("OIII"))));
            Assert.Empty(Check(target));
        }

        [Fact]
        public void ExposuresOutsideTheBlocksRunOnEveryPass() {
            var target = Container("M33", Container("Loop", UntilTime, Wait,
                Block("Block Ha", "Ha", Iterations),
                Exposure("L"),
                Container("Flats", Exposure("L"))));
            var findings = Check(target);
            AssertOne(findings, FindingLevel.Hint, "'Smart Exposure' in 'Loop' is not in an NSTN block");
            AssertOne(findings, FindingLevel.Hint, "'Flats' in 'Loop' is not in an NSTN block");
        }

        [Fact]
        public void TheConditionsOwnIssuesAreRepeated() {
            var broken = Nstn("Ha") with { Issues = new[] { "No sky quality set" } };
            var target = Container("M33", Container("Loop", UntilTime, Wait, Container("Block Ha", broken, Iterations, Exposure("Ha"))));
            AssertOne(Check(target), FindingLevel.Warning, "No sky quality set");
        }

        [Fact]
        public void WithoutAnyNstnBlockThereIsOneHint() {
            var findings = Check(Container("M33", Slew, Container("Loop", UntilTime, Exposure("Ha"))));
            Assert.Single(findings);
            Assert.Equal(FindingLevel.Hint, findings[0].Level);
            Assert.Contains("No block below this target", findings[0].Message);
        }

        [Fact]
        public void TheConditionOnTheTargetItselfIsReported() {
            var target = Container("M33", Nstn("Ha"), Slew, Container("Loop", UntilTime, Wait, Block("Block Ha", "Ha", Iterations)));
            AssertOne(Check(target), FindingLevel.Warning, "sits on the target");
        }

        [Fact]
        public void AMissingMeridianFlipIsReportedWhenTheTargetTransitsInTheDark() {
            var night = Night();
            var transit = night.Bundle.Target.TransitMs;
            Assert.Contains(night.Usable, i => i.Start <= transit && transit < i.End);

            AssertOne(Check(Recommended(), night), FindingLevel.Hint, "No 'Meridian Flip' trigger");
            // A trigger on the loop works as well as one on the target.
            var onLoop = Container("M33", Slew,
                Container("Loop", UntilTime, SeqTriggerKind.MeridianFlip, Wait, Block("Block Ha", "Ha", Iterations)));
            Assert.Empty(Check(onLoop, night));
            // ... but not one on an unrelated container next to the blocks.
            var aside = Container("M33", Slew, Container("Other", SeqTriggerKind.MeridianFlip),
                Container("Loop", UntilTime, Wait, Block("Block Ha", "Ha", Iterations)));
            AssertOne(Check(aside, night), FindingLevel.Hint, "No 'Meridian Flip' trigger");
        }

        [Fact]
        public void ATargetThatStaysLowIsReported() {
            var findings = Check(Recommended(SeqTriggerKind.MeridianFlip), Night(dec: -40));
            AssertOne(findings, FindingLevel.Warning, "does not rise above 20°");
        }

        [Fact]
        public void AWindowThatNeverOpensAtTheSiteIsAWarning() {
            var findings = Check(Recommended(SeqTriggerKind.MeridianFlip), Night(sqm: 18.0));
            Assert.Equal(2, findings.Count(f => f.Level == FindingLevel.Warning && f.Message.Contains("never opens, on any night")));
        }

        [Fact]
        public void AWindowClosedTonightIsAHint() {
            // Full moon next to M33 (golden case mid-north-fullmoon): no window opens all night.
            var night = Night(date: "2025-10-07");
            Assert.All(night.Bands, b => Assert.Empty(b.Segments));
            Assert.All(night.Bands, b => Assert.False(b.NeverOpensAtSite));
            var findings = Check(Recommended(SeqTriggerKind.MeridianFlip), night);
            Assert.Equal(2, findings.Count(f => f.Level == FindingLevel.Hint && f.Message.Contains("does not open tonight")));
            AssertOne(findings, FindingLevel.Hint, "(OIII 7 nm): its window does not open tonight");
        }

        [Fact]
        public void ABlockNamedAfterItsBandIsNotLabelledTwice() {
            var target = Container("M33", Container("Loop", UntilTime, Wait, Container("Ha 7 nm", Nstn("Ha"), Iterations, Switch("Ha"))));
            AssertOne(Check(target), FindingLevel.Warning, "Block 'Ha 7 nm' contains no exposure");
            var other = Container("M33", Container("Loop", UntilTime, Wait, Container("Block", Nstn("Ha"), Iterations, Switch("Ha"))));
            AssertOne(Check(other), FindingLevel.Warning, "Block 'Block' (Ha 7 nm) contains no exposure");
        }

        [Fact]
        public void WarningsComeFirst() {
            var target = Container("M33", Container("Loop", UntilTime, Block("Block OIII", "OIII"), Block("Block Ha", "Ha")));
            var levels = Check(target).Select(f => f.Level).ToList();
            Assert.Contains(FindingLevel.Warning, levels);
            Assert.Contains(FindingLevel.Hint, levels);
            Assert.Equal(levels.OrderBy(l => l), levels);
        }
    }
}
