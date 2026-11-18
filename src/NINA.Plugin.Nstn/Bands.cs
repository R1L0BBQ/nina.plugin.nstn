using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Nstn.Core;

namespace NINA.Plugin.Nstn {

    /// <summary>
    /// An entry of a selector. The selectors keep one list instance each (a ComboBox
    /// handed a new list resets its selection), so the label is read when shown and
    /// announced again when the language changes - also in the closed ComboBox.
    /// The lists are static, so subscribing to the static event leaks nothing.
    /// </summary>
    public abstract class LocalizedChoice : INotifyPropertyChanged {

        protected LocalizedChoice() {
            Text.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }

        public abstract string Label { get; }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>One entry of the band selector in the condition.</summary>
    public sealed class BandChoice : LocalizedChoice {

        public BandChoice(string key) {
            Key = key;
        }

        public string Key { get; }

        public override string Label => BandNames.Long(Key);
    }

    /// <summary>One entry of the filter selector: a catalog filter, or "custom" with an empty name.</summary>
    public sealed class PresetChoice : LocalizedChoice {

        private readonly Func<string> label;

        public PresetChoice(string name, Func<string> label) {
            Name = name;
            this.label = label;
        }

        public string Name { get; }

        public override string Label => label();
    }

    public static class Presets {

        public static IReadOnlyList<PresetChoice> All { get; } =
            new[] { new PresetChoice(string.Empty, () => Text.T("Preset_Custom")) }
                .Concat(FilterCatalog.All.Select(f => new PresetChoice(f.Name,
                    () => f.Kind == FilterKind.Duo ? Text.T("Preset_Duo", f.Name) : Text.T("Preset_Set", f.Name))))
                .ToList();
    }

    /// <summary>The bands a condition can judge, keyed as in the night model.</summary>
    public static class Bands {

        public const string Broadband = "RGB";

        public static IReadOnlyList<BandChoice> All { get; } = new[] {
            new BandChoice(Broadband),
            new BandChoice("Ha"),
            new BandChoice("OIII"),
            new BandChoice("SII"),
            new BandChoice("Hb"),
        };

        /// <summary>The emission lines alone, for a filter that is not broadband.</summary>
        public static IReadOnlyList<BandChoice> Lines { get; } = All.Where(b => b.Key != Broadband).ToList();

        // One list instance per catalog filter. A ComboBox handed a new list resets its
        // selection and writes null back; the real line then has to win a race against
        // that reset - the band selector showed empty after a block's filter changed.
        private static readonly ConcurrentDictionary<string, IReadOnlyList<BandChoice>> PresetLines = new();

        /// <summary>The lines a catalog filter passes, always the same list instance.</summary>
        public static IReadOnlyList<BandChoice> Of(CatalogFilter preset) =>
            PresetLines.GetOrAdd(preset.Name, _ => All.Where(b => preset.Find(b.Key) != null).ToList());

        public const string DefaultKey = "Ha";

        public const double DefaultBandwidthNm = 7.0;

        /// <summary>Unknown keys - a hand-edited or future sequence file - fall back to H-alpha.</summary>
        public static string Normalize(string key) =>
            All.Any(b => string.Equals(b.Key, key, StringComparison.Ordinal)) ? key : DefaultKey;

        public static string ShortLabel(string key) => BandNames.Short(key);

        public static EvalFilter ToFilter(string key, double bandwidthNm) =>
            key == Broadband
                ? NightModel.BroadbandFilter()
                : NightModel.LineFilter(Normalize(key), bandwidthNm);
    }
}
