using System;
using System.Collections.Generic;
using System.Linq;
using Nstn.Core;

namespace NINA.Plugin.Nstn {

    /// <summary>One entry of the band selector in the condition.</summary>
    public sealed record BandChoice(string Key, string Label);

    /// <summary>One entry of the filter selector: a catalog filter, or "custom" with an empty name.</summary>
    public sealed record PresetChoice(string Name, string Label);

    public static class Presets {

        public static IReadOnlyList<PresetChoice> All { get; } =
            new[] { new PresetChoice(string.Empty, "Custom: band and bandwidth by hand") }
                .Concat(FilterCatalog.All.Select(f => new PresetChoice(f.Name, $"{(f.Kind == FilterKind.Duo ? "Duo" : "Set")} · {f.Name}")))
                .ToList();
    }

    /// <summary>The bands a condition can judge, keyed as in the night model.</summary>
    public static class Bands {

        public const string Broadband = "RGB";

        public static IReadOnlyList<BandChoice> All { get; } = new[] {
            new BandChoice(Broadband, "L / RGB (broadband)"),
            new BandChoice("Ha", "H-alpha 656 nm"),
            new BandChoice("OIII", "O III 501 nm"),
            new BandChoice("SII", "S II 672 nm"),
            new BandChoice("Hb", "H-beta 486 nm"),
        };

        /// <summary>The emission lines alone, for a filter that is not broadband.</summary>
        public static IReadOnlyList<BandChoice> Lines { get; } = All.Where(b => b.Key != Broadband).ToList();

        public const string DefaultKey = "Ha";

        public const double DefaultBandwidthNm = 7.0;

        /// <summary>Unknown keys - a hand-edited or future sequence file - fall back to H-alpha.</summary>
        public static string Normalize(string key) =>
            All.Any(b => string.Equals(b.Key, key, StringComparison.Ordinal)) ? key : DefaultKey;

        public static string ShortLabel(string key) => key switch {
            Broadband => "L/RGB",
            "Ha" => "H-alpha",
            "OIII" => "O III",
            "SII" => "S II",
            "Hb" => "H-beta",
            _ => key,
        };

        public static EvalFilter ToFilter(string key, double bandwidthNm) =>
            key == Broadband
                ? NightModel.BroadbandFilter()
                : NightModel.LineFilter(Normalize(key), bandwidthNm);
    }
}
