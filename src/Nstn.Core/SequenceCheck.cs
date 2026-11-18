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
                Warn($"The NSTN condition sits on the target '{target.Name}' itself: the whole target, slew and centring included, then only runs while that one window is open. Put it on a block inside the target instead.");
            }
            if (blocks.Count == 0) {
                Hint("No block below this target has a 'Loop while NSTN window open' condition yet. Give every filter its own block (an instruction set with a filter switch and the exposures) and put the condition on that block.");
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
                ? $"'{block.Name}'"
                : $"'{block.Name}' ({label})";
        }

        private static void CheckBlock(SeqNode block, SequenceCheckContext context, Action<string> warn, Action<string> hint) {
            var condition = block.Nstn!;
            foreach (var issue in condition.Issues) {
                warn($"Block {Describe(block)}: {issue}");
            }

            var exposures = block.Descendants().Where(n => n.Kind == SeqItemKind.Exposure).ToList();
            if (exposures.Count == 0) {
                warn($"Block {Describe(block)} contains no exposure: its window only decides when nothing is taken.");
            }

            if (block.Descendants().Any(n => n.Kind == SeqItemKind.NstnWait)) {
                warn($"'Wait for NSTN window' sits inside block {Describe(block)}. It belongs at the top of the loop that holds the blocks, not inside one of them.");
            }

            if (!condition.FollowsBlockFilter && condition.Band != null) {
                var filters = block.Descendants()
                    .Where(n => n.Kind is SeqItemKind.SwitchFilter or SeqItemKind.Exposure && !string.IsNullOrWhiteSpace(n.Filter))
                    .Select(n => n.Filter!)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var filter in filters) {
                    var lines = context.LinesOfFilter(filter);
                    if (lines != null && !lines.Contains(condition.Band.Key)) {
                        warn($"Block {Describe(block)} uses filter '{filter}', but its condition watches {condition.Label}: the window of another band decides when '{filter}' is exposed. Choose \"From this block's filter\" or the matching band.");
                    }
                }
            }

            if (context.Night is NightInfo night && condition.Band != null) {
                var window = night.Bands.FirstOrDefault(b => b.Band == condition.Band)
                    ?? new BandWindow(condition.Band, NightModel.WorthwhileSegments(night.Bundle.Sky, condition.Band, night.Settings),
                        night.Settings.ThresholdFor(condition.Band),
                        night.Bundle.Sky.SqmZenith < NightModel.MinimumSkyBrightness(condition.Band, night.Settings));
                if (window.NeverOpensAtSite) {
                    warn($"Block {Describe(block)}: at this site's sky quality the window never opens, on any night. Lower the threshold in the plugin options or drop the block.");
                } else if (window.Segments.Count == 0) {
                    hint($"Block {Describe(block)}: its window does not open tonight, so the block is skipped all night.");
                }
            }
        }

        private static void CheckLoop(SeqNode loop, Action<string> warn, Action<string> hint) {
            var blocks = loop.Children.Where(c => c.IsNstnBlock).ToList();
            var loops = loop.Conditions.Count > 0;

            if (!loops) {
                warn($"'{loop.Name}' has no loop condition, so N.I.N.A. runs it exactly once: every block gets one turn and the target is done, however long the night. Add 'Loop Until Time' (e.g. morning twilight) to '{loop.Name}'.");
            }

            var waitIndex = loop.Children.FindIndex(c => c.Kind == SeqItemKind.NstnWait);
            if (loops && waitIndex < 0) {
                warn($"'{loop.Name}' loops but has no 'Wait for NSTN window'. While every window is closed - at dusk, or when the moon is up - N.I.N.A. 3.2 then races through the loop without a pause and floods the log. Put 'Wait for NSTN window' first in '{loop.Name}'.");
            } else if (waitIndex > 0) {
                hint($"Move 'Wait for NSTN window' to the top of '{loop.Name}': whatever stands before it runs on every pass, even while all windows are closed.");
            }

            if (blocks.Count > 1) {
                foreach (var block in blocks.Where(b => b.Conditions.All(c => c.Kind != SeqConditionKind.LoopForIterations))) {
                    hint($"Block {Describe(block)} has no 'Loop For Iterations': it keeps its window to itself for as long as it is open, and the blocks after it only run once it closes. For a round robin add 'Loop For Iterations' (1 = one turn per pass).");
                }
            }

            foreach (var item in loop.Children) {
                var stray = item.Kind == SeqItemKind.Exposure
                    || (item.Kind == SeqItemKind.Container && !item.IsNstnBlock && item.Descendants().Any(n => n.Kind == SeqItemKind.Exposure));
                if (stray) {
                    hint($"'{item.Name}' in '{loop.Name}' is not in an NSTN block: it is exposed on every pass, whatever the sky.");
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
                hint($"No 'Meridian Flip' trigger: the target crosses the meridian at {context.Clock(transit)}, during the usable time. An equatorial mount needs the trigger on the target or on the sequence.");
            }
        }

        private static void CheckTheNight(SeqNode target, SequenceCheckContext context, Action<string> warn) {
            if (context.Night is not NightInfo night) {
                return;
            }
            var limit = night.Settings.MinAltitudeDeg;
            if (night.TargetUp.Count == 0) {
                warn($"'{target.Name}' does not rise above {limit:0}° tonight: no window can open.");
            } else if (night.Usable.Count == 0 && night.Bundle.Night.HasNight) {
                warn($"'{target.Name}' is above {limit:0}° only outside the astronomical night.");
            }
        }
    }
}
