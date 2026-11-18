// A look at how a target's sequence is built, against the rules N.I.N.A.'s
// sequencer actually follows: conditions on a container are loop conditions, a
// container without one runs once, 3.2 has no idle pause, triggers only fire
// between items. The plugin translates N.I.N.A.'s tree into these plain nodes, so
// the rules can be tested without N.I.N.A.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nstn.Core {

    public enum SeqItemKind { Container, Exposure, NstnWait, SwitchFilter, Other }

    public enum SeqConditionKind { NstnWindow, LoopForIterations, LoopUntilTime, Other }

    public enum SeqTriggerKind { MeridianFlip, Autofocus, Dither, Other }

    public sealed record SeqCondition(SeqConditionKind Kind, string Name) {
        /// <summary>The band an NSTN condition judges; null while it follows an unresolved block filter.</summary>
        public EvalFilter? Band { get; init; }

        /// <summary>Short text for messages, such as "O III 7 nm".</summary>
        public string Label { get; init; } = string.Empty;

        public bool FollowsBlockFilter { get; init; }

        /// <summary>The condition's own complaints - it knows its configuration best.</summary>
        public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    }

    public sealed class SeqNode {

        public SeqNode(string name, SeqItemKind kind) {
            Name = name;
            Kind = kind;
        }

        public string Name { get; }

        public SeqItemKind Kind { get; }

        /// <summary>The filter a filter switch or an exposure selects, by its N.I.N.A. name.</summary>
        public string? Filter { get; init; }

        public List<SeqCondition> Conditions { get; } = new();

        public List<SeqTriggerKind> Triggers { get; } = new();

        public List<SeqNode> Children { get; } = new();

        public SeqCondition? Nstn => Conditions.FirstOrDefault(c => c.Kind == SeqConditionKind.NstnWindow);

        public bool IsNstnBlock => Kind == SeqItemKind.Container && Nstn != null;

        public IEnumerable<SeqNode> Descendants() {
            foreach (var child in Children) {
                yield return child;
                foreach (var below in child.Descendants()) {
                    yield return below;
                }
            }
        }
    }

    public enum FindingLevel { Warning, Hint }

    public sealed record SequenceFinding(FindingLevel Level, string Message);

    /// <summary>What the check needs to know beyond the target's own tree.</summary>
    public sealed record SequenceCheckContext(
        bool MeridianFlipAbove,                                  // a trigger on a container above the target
        NightInfo? Night,
        Func<string, IReadOnlyCollection<string>?> LinesOfFilter, // band keys a N.I.N.A. filter passes; null if unassigned
        Func<double, string> Clock);

    public static class SequenceCheck {

        public static IReadOnlyList<SequenceFinding> Check(SeqNode target, SequenceCheckContext context) {
            var findings = new List<SequenceFinding>();
            void Warn(string message) => findings.Add(new SequenceFinding(FindingLevel.Warning, message));
            void Hint(string message) => findings.Add(new SequenceFinding(FindingLevel.Hint, message));

            var parents = new Dictionary<SeqNode, SeqNode>();
            void Link(SeqNode node) {
                foreach (var child in node.Children) {
                    parents[child] = node;
                    Link(child);
                }
            }
            Link(target);

            CheckTheNight(target, context, Warn);

            var blocks = target.Descendants().Where(n => n.IsNstnBlock).ToList();
            if (target.IsNstnBlock) {
                Warn(Text.T("Check_OnTarget", Text.Quote(target.Name)));
            }
            if (blocks.Count == 0) {
                Hint(Text.T("Check_NoBlocks"));
                CheckTriggers(target, parents, context, Hint, blocks);
                return Ordered(findings);
            }

            foreach (var block in blocks) {
                CheckBlock(block, context, Warn, Hint);
            }

            foreach (var loop in blocks.Select(b => parents.TryGetValue(b, out var p) ? p : null).Where(p => p != null).Distinct()) {
                CheckLoop(loop!, Warn, Hint);
            }

            CheckTriggers(target, parents, context, Hint, blocks);
            return Ordered(findings);
        }

        private static IReadOnlyList<SequenceFinding> Ordered(List<SequenceFinding> findings) =>
            findings.OrderBy(f => f.Level).ToList();

        private static string Describe(SeqNode block) {
            var label = block.Nstn?.Label;
            return string.IsNullOrEmpty(label) || block.Name.Contains(label, StringComparison.OrdinalIgnoreCase)
                ? Text.Quote(block.Name)
                : Text.T("Check_BlockWithBand", Text.Quote(block.Name), label);
        }

        private static void CheckBlock(SeqNode block, SequenceCheckContext context, Action<string> warn, Action<string> hint) {
            var condition = block.Nstn!;
            foreach (var issue in condition.Issues) {
                warn(Text.T("Check_BlockIssue", Describe(block), issue));
            }

            var exposures = block.Descendants().Where(n => n.Kind == SeqItemKind.Exposure).ToList();
            if (exposures.Count == 0) {
                warn(Text.T("Check_NoExposure", Describe(block)));
            }

            if (block.Descendants().Any(n => n.Kind == SeqItemKind.NstnWait)) {
                warn(Text.T("Check_WaitInBlock", Describe(block)));
            }

            if (!condition.FollowsBlockFilter && condition.Band != null) {
                var filters = block.Descendants()
                    .Where(n => n.Kind is SeqItemKind.SwitchFilter or SeqItemKind.Exposure && !string.IsNullOrWhiteSpace(n.Filter))
                    .Select(n => n.Filter!)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var filter in filters) {
                    var lines = context.LinesOfFilter(filter);
                    if (lines != null && !lines.Contains(condition.Band.Key)) {
                        warn(Text.T("Check_FilterMismatch", Describe(block), Text.Quote(filter), condition.Label));
                    }
                }
            }

            if (context.Night is NightInfo night && condition.Band != null) {
                var window = night.Bands.FirstOrDefault(b => b.Band == condition.Band)
                    ?? new BandWindow(condition.Band, NightModel.WorthwhileSegments(night.Bundle.Sky, condition.Band, night.Settings),
                        night.Settings.ThresholdFor(condition.Band),
                        night.Bundle.Sky.SqmZenith < NightModel.MinimumSkyBrightness(condition.Band, night.Settings));
                if (window.NeverOpensAtSite) {
                    warn(Text.T("Check_NeverAtSite", Describe(block)));
                } else if (window.Segments.Count == 0) {
                    hint(Text.T("Check_ClosedTonight", Describe(block)));
                }
            }
        }

        private static void CheckLoop(SeqNode loop, Action<string> warn, Action<string> hint) {
            var blocks = loop.Children.Where(c => c.IsNstnBlock).ToList();
            var loops = loop.Conditions.Count > 0;

            if (!loops) {
                warn(Text.T("Check_NoLoopCondition", Text.Quote(loop.Name)));
            }

            var waitIndex = loop.Children.FindIndex(c => c.Kind == SeqItemKind.NstnWait);
            if (loops && waitIndex < 0) {
                warn(Text.T("Check_NoWait", Text.Quote(loop.Name)));
            } else if (waitIndex > 0) {
                hint(Text.T("Check_WaitNotFirst", Text.Quote(loop.Name)));
            }

            if (blocks.Count > 1) {
                foreach (var block in blocks.Where(b => b.Conditions.All(c => c.Kind != SeqConditionKind.LoopForIterations))) {
                    hint(Text.T("Check_Priority", Describe(block)));
                }
            }

            foreach (var item in loop.Children) {
                var stray = item.Kind == SeqItemKind.Exposure
                    || (item.Kind == SeqItemKind.Container && !item.IsNstnBlock && item.Descendants().Any(n => n.Kind == SeqItemKind.Exposure));
                if (stray) {
                    hint(Text.T("Check_Stray", Text.Quote(item.Name), Text.Quote(loop.Name)));
                }
            }
        }

        private static void CheckTriggers(SeqNode target, Dictionary<SeqNode, SeqNode> parents, SequenceCheckContext context, Action<string> hint, List<SeqNode> blocks) {
            if (context.Night is not NightInfo night) {
                return;
            }
            // A trigger counts where it can fire for the exposures: on the target, on a
            // container between it and a block, or above the target.
            var containers = new HashSet<SeqNode> { target };
            foreach (var block in blocks) {
                for (var node = block; node != null; node = parents.TryGetValue(node, out var p) ? p : null) {
                    containers.Add(node);
                }
            }
            var flip = context.MeridianFlipAbove || containers.Any(c => c.Triggers.Contains(SeqTriggerKind.MeridianFlip));
            var transit = night.Bundle.Target.TransitMs;
            if (!flip && night.Usable.Any(i => i.Start <= transit && transit < i.End)) {
                hint(Text.T("Check_NoFlip", context.Clock(transit)));
            }
        }

        private static void CheckTheNight(SeqNode target, SequenceCheckContext context, Action<string> warn) {
            if (context.Night is not NightInfo night) {
                return;
            }
            var limit = night.Settings.MinAltitudeDeg;
            if (night.TargetUp.Count == 0) {
                warn(Text.T("Check_TooLow", Text.Quote(target.Name), limit));
            } else if (night.Usable.Count == 0 && night.Bundle.Night.HasNight) {
                warn(Text.T("Check_OnlyOutsideNight", Text.Quote(target.Name), limit));
            }
        }
    }
}
