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
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.Trigger;
using Nstn.Core;

namespace NINA.Plugin.Nstn.Instructions {

    /// <summary>
    /// N.I.N.A.'s sequence tree as the plain nodes of <see cref="SequenceCheck"/>.
    /// Every call here takes N.I.N.A.'s container lock: call it before taking a lock
    /// of your own, never while holding one (see NstnWindowCondition.Refresh).
    /// </summary>
    internal static class SequenceTree {

        private const int MaxDepth = 12;

        /// <summary>The nearest target container at or above a container.</summary>
        public static IDeepSkyObjectContainer TargetOf(ISequenceContainer container) {
            for (var current = container; current != null; current = current.Parent) {
                if (current is IDeepSkyObjectContainer target) {
                    return target;
                }
            }
            return null;
        }

        /// <summary>A meridian flip trigger on a container above the target - the sequence root included.</summary>
        public static bool MeridianFlipAbove(ISequenceContainer target) {
            for (var current = target?.Parent; current != null; current = current.Parent) {
                if (current is ITriggerable triggerable
                    && triggerable.GetTriggersSnapshot().Any(t => t is IMeridianFlipTrigger && t.Status != SequenceEntityStatus.DISABLED)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The enabled NSTN conditions at or below a container.</summary>
        public static List<NstnWindowCondition> NstnConditions(ISequenceContainer container, int depth = 0) {
            var found = new List<NstnWindowCondition>();
            if (container == null || depth > MaxDepth) {
                return found;
            }
            if (container is IConditionable conditionable) {
                found.AddRange(conditionable.GetConditionsSnapshot().OfType<NstnWindowCondition>()
                    .Where(c => c.Status != SequenceEntityStatus.DISABLED));
            }
            foreach (var child in container.GetItemsSnapshot().OfType<ISequenceContainer>()) {
                if (child.Status != SequenceEntityStatus.DISABLED) {
                    found.AddRange(NstnConditions(child, depth + 1));
                }
            }
            return found;
        }

        public static SeqNode Convert(ISequenceContainer container, int depth = 0) {
            var node = new SeqNode(container.Name, SeqItemKind.Container);
            if (container is IConditionable conditionable) {
                foreach (var condition in conditionable.GetConditionsSnapshot()) {
                    if (condition.Status != SequenceEntityStatus.DISABLED) {
                        node.Conditions.Add(ConditionOf(condition));
                    }
                }
            }
            if (container is ITriggerable triggerable) {
                foreach (var trigger in triggerable.GetTriggersSnapshot()) {
                    if (trigger.Status != SequenceEntityStatus.DISABLED) {
                        node.Triggers.Add(TriggerOf(trigger));
                    }
                }
            }
            if (depth < MaxDepth) {
                foreach (var item in container.GetItemsSnapshot()) {
                    if (item.Status != SequenceEntityStatus.DISABLED) {
                        node.Children.Add(ItemOf(item, depth + 1));
                    }
                }
            }
            return node;
        }

        public static string LabelOf(NstnWindowCondition condition) =>
            condition.IsNarrowband
                ? $"{condition.ShortLabel} {condition.BandwidthNm.ToString("0.#", CultureInfo.InvariantCulture)} nm"
                : condition.ShortLabel;

        private static SeqNode ItemOf(ISequenceItem item, int depth) => item switch {
            ISequenceContainer container => Convert(container, depth),
            NstnWaitForWindow wait => new SeqNode(wait.Name, SeqItemKind.NstnWait),
            SwitchFilter switchFilter => new SeqNode(switchFilter.Name, SeqItemKind.SwitchFilter) { Filter = switchFilter.Filter?.Name?.Trim() },
            IExposureItem => new SeqNode(item.Name, SeqItemKind.Exposure),
            _ => new SeqNode(item.Name, SeqItemKind.Other),
        };

        private static SeqCondition ConditionOf(ISequenceCondition condition) => condition switch {
            NstnWindowCondition nstn => new SeqCondition(SeqConditionKind.NstnWindow, nstn.Name) {
                Band = Bands.ToFilter(nstn.Line, nstn.BandwidthNm),
                Label = LabelOf(nstn),
                FollowsBlockFilter = nstn.FollowsBlockFilter,
                Issues = nstn.Issues.ToList(),
            },
            LoopCondition => new SeqCondition(SeqConditionKind.LoopForIterations, condition.Name),
            TimeCondition => new SeqCondition(SeqConditionKind.LoopUntilTime, condition.Name),
            _ => new SeqCondition(SeqConditionKind.Other, condition.Name),
        };

        private static SeqTriggerKind TriggerOf(ISequenceTrigger trigger) {
            if (trigger is IMeridianFlipTrigger) {
                return SeqTriggerKind.MeridianFlip;
            }
            var type = trigger.GetType();
            if (type.Namespace?.EndsWith(".Autofocus", StringComparison.Ordinal) == true) {
                return SeqTriggerKind.Autofocus;
            }
            return type.Name.Contains("Dither", StringComparison.Ordinal) ? SeqTriggerKind.Dither : SeqTriggerKind.Other;
        }

        /// <summary>The band keys a N.I.N.A. filter passes, from the profile's assignments; null if unassigned.</summary>
        public static Func<string, IReadOnlyCollection<string>> LinesOfFilter(IReadOnlyList<FilterAssignment> assignments) =>
            name => {
                var assignment = FilterAssignments.Find(assignments, name);
                if (assignment == null || !FilterAssignments.IsValid(assignment)) {
                    return null;
                }
                if (assignment.IsBroadband) {
                    return new[] { Bands.Broadband };
                }
                var preset = FilterCatalog.Find(assignment.Preset);
                return preset != null ? preset.Lines.Select(l => l.Line).ToList() : new[] { assignment.Line };
            };
    }
}
