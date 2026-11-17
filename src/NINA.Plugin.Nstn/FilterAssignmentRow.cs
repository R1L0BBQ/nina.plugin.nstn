using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Nstn.Core;

namespace NINA.Plugin.Nstn {

    /// <summary>
    /// One filter of the profile on the options page: what it passes. Changes are
    /// saved through the callback right away, like every other NINA option.
    /// </summary>
    public sealed class FilterAssignmentRow : INotifyPropertyChanged {

        public const string NotAssigned = "~none";
        public const string Broadband = "~broadband";
        public const string Custom = "~custom";

        public static IReadOnlyList<PresetChoice> Choices { get; } =
            new[] {
                new PresetChoice(NotAssigned, "— not assigned —"),
                new PresetChoice(Broadband, "Broadband: L, R, G, B or no filter"),
                new PresetChoice(Custom, "Custom: line and bandwidth by hand"),
            }
            .Concat(Presets.All.Where(p => p.Name.Length > 0))
            .ToList();

        private readonly Action changed;
        private string choice = NotAssigned;
        private string line = Bands.DefaultKey;
        // Zero until someone enters a width: a custom band gets no invented default,
        // and an assignment without a width is not saved.
        private double bandwidthNm;

        public FilterAssignmentRow(string ninaName, FilterAssignment existing, Action changed) {
            NinaName = ninaName;
            Suggestion = FilterAssignments.Suggest(ninaName);
            this.changed = changed;
            if (existing != null) {
                Load(existing);
            }
        }

        public string NinaName { get; }

        public FilterAssignment Suggestion { get; }

        public string Choice {
            get => choice;
            set {
                if (value == null || value == choice) {
                    return;
                }
                choice = value;
                var preset = FilterCatalog.Find(choice);
                if (preset != null) {
                    // As in the condition: a duo starts on its weak line, a set keeps the line if it has it.
                    var entry = preset.WeakLine ?? preset.Find(line) ?? preset.Lines[0];
                    line = entry.Line;
                    bandwidthNm = entry.BandwidthNm;
                } else if (choice == Custom && line == Bands.Broadband) {
                    line = Bands.DefaultKey;
                }
                RaiseAll();
                changed();
            }
        }

        public string Line {
            get => line;
            set {
                if (string.IsNullOrEmpty(value) || value == line) {
                    return;
                }
                line = value;
                var entry = FilterCatalog.Find(choice)?.Find(line);
                if (entry != null) {
                    bandwidthNm = entry.BandwidthNm;
                }
                RaiseAll();
                changed();
            }
        }

        public double BandwidthNm {
            get => bandwidthNm;
            set {
                if (!IsBandwidthEditable || !(value >= 0) || double.IsInfinity(value)) {
                    Raise();
                    return;
                }
                bandwidthNm = value;
                RaiseAll();
                changed();
            }
        }

        public IReadOnlyList<BandChoice> AvailableBands {
            get {
                var preset = FilterCatalog.Find(choice);
                if (preset != null) {
                    return Bands.All.Where(b => preset.Find(b.Key) != null).ToList();
                }
                return choice == Custom ? Bands.Lines : Array.Empty<BandChoice>();
            }
        }

        public bool HasLine => choice == Custom || FilterCatalog.Find(choice) != null;

        public bool IsBandwidthEditable => choice == Custom;

        /// <summary>What the row amounts to, or why it is not saved yet.</summary>
        public string Result {
            get {
                var assignment = ToAssignment();
                if (assignment != null) {
                    return "→ " + assignment.Describe();
                }
                return choice == Custom ? "enter the bandwidth" : string.Empty;
            }
        }

        public string SuggestionText => Suggestion == null ? string.Empty : "Suggestion: " + Suggestion.Describe();

        public bool CanTakeSuggestion => Suggestion != null && choice == NotAssigned;

        public void TakeSuggestion() {
            if (CanTakeSuggestion) {
                Load(Suggestion);
                RaiseAll();
                changed();
            }
        }

        public FilterAssignment ToAssignment() {
            FilterAssignment result = choice switch {
                NotAssigned => null,
                Broadband => new FilterAssignment(NinaName, string.Empty, Bands.Broadband, NightModel.VWidthNm),
                Custom => new FilterAssignment(NinaName, string.Empty, line, bandwidthNm),
                _ => new FilterAssignment(NinaName, choice, line, bandwidthNm),
            };
            return FilterAssignments.IsValid(result) ? result : null;
        }

        private void Load(FilterAssignment assignment) {
            if (assignment.IsBroadband) {
                choice = Broadband;
                line = Bands.Broadband;
                bandwidthNm = NightModel.VWidthNm;
            } else {
                choice = assignment.Preset.Length > 0 ? assignment.Preset : Custom;
                line = assignment.Line;
                bandwidthNm = assignment.BandwidthNm;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void RaiseAll() {
            foreach (var name in new[] { nameof(Choice), nameof(Line), nameof(BandwidthNm), nameof(AvailableBands),
                                         nameof(HasLine), nameof(IsBandwidthEditable), nameof(Result), nameof(CanTakeSuggestion) }) {
                Raise(name);
            }
        }
    }
}
