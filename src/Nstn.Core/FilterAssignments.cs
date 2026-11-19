// What a filter of the N.I.N.A. profile passes, stated once per profile: a filter
// name from the filter wheel list, and the band NSTN judges for it. A condition set
// to "this block's filter" looks the name up here, so changing the filter of a
// Smart Exposure changes the band with it.
//
// Suggestions follow FITSORA's rule: offered, never applied on their own, and a
// bandwidth is never invented - "Ha" without a number gets no suggestion, because a
// default width would decide silently when the window opens.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nstn.Core {

    /// <param name="NinaName">Filter name as in the N.I.N.A. profile.</param>
    /// <param name="Preset">Catalog filter name, empty for a band set by hand.</param>
    /// <param name="Line">Night model key: RGB for broadband, Ha, OIII, SII, Hb.</param>
    public sealed record FilterAssignment(string NinaName, string Preset, string Line, double BandwidthNm) {

        // Derived values stay out of the stored JSON: the profile keeps what was
        // chosen, not what follows from it today.
        [JsonIgnore]
        public bool IsBroadband => Line == "RGB";

        [JsonIgnore]
        public EvalFilter Band => IsBroadband
            ? NightModel.BroadbandFilter()
            : NightModel.LineFilter(Line, BandwidthNm);

        /// <summary>Readable form for the options page and the condition, e.g. "O III 6 nm (Askar D1 …)".</summary>
        public string Describe() {
            var band = IsBroadband
                ? Text.T("Assign_Broadband")
                : BandNames.WithBandwidth(Line, BandwidthNm);
            return Preset.Length > 0 ? $"{band} ({Preset})" : band;
        }
    }

    public static class FilterAssignments {

        private static readonly string[] LineKeys = { "RGB", "Ha", "OIII", "SII", "Hb" };

        // Product tokens in a compacted name (lower case, no spaces, dashes, slashes,
        // underscores) to the catalog entry. Only names that identify one entry.
        private static readonly (string Token, string Preset)[] Products = {
            ("askard1", "Askar D1 - H-Alpha & O-III"),
            ("askard2", "Askar D2 - S-II & O-III"),
            ("askarc1", "Askar C1 - H-Alpha & O-III"),
            ("askarc2", "Askar C2 - S-II & O-III"),
            ("lenhance", "Optolong L-eNhance"),
            ("lextreme", "Optolong L-eXtreme (7 nm)"),
            ("lultimate", "Optolong L-Ultimate (3 nm)"),
            ("lpara", "Optolong L-Para (10 nm)"),
            ("lsynergy", "Optolong L-Synergy"),
            ("sv2203nm", "SVBONY SV220 (3 nm)"),
            ("sv2207nm", "SVBONY SV220 (7 nm)"),
        };

        // Names that are ambiguous on purpose: Askar sells a "Super D1" at 8.5/6.5 nm
        // next to the D1 at 6/6 nm, and no name tells the ALP-T variants apart.
        private static readonly string[] Ambiguous = { "super", "alpt" };

        private static readonly HashSet<string> BroadbandNames = new HashSet<string>(StringComparer.Ordinal) {
            "l", "lum", "luminance", "r", "red", "g", "green", "b", "blue", "rgb", "osc",
            "clear", "none", "nofilter", "keinfilter", "uvir", "luvir", "uvircut", "ircut",
        };

        private static readonly Regex BandwidthInName = new Regex(@"(\d+(?:[.,]\d+)?)\s*nm", RegexOptions.IgnoreCase);

        public static FilterAssignment? Find(IEnumerable<FilterAssignment> assignments, string? ninaName) =>
            string.IsNullOrWhiteSpace(ninaName)
                ? null
                : assignments.FirstOrDefault(a => string.Equals(a.NinaName.Trim(), ninaName.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>A starting point for one filter name, or null when the name does not say enough.</summary>
        public static FilterAssignment? Suggest(string ninaName) {
            var compact = Regex.Replace(ninaName, @"[\s_/\-.]", string.Empty).ToLowerInvariant();
            if (compact.Length == 0) {
                return null;
            }
            if (BroadbandNames.Contains(compact)) {
                return new FilterAssignment(ninaName, string.Empty, "RGB", NightModel.VWidthNm);
            }
            if (!Ambiguous.Any(compact.Contains)) {
                foreach (var (token, presetName) in Products) {
                    if (compact.Contains(token, StringComparison.Ordinal)) {
                        var preset = FilterCatalog.Find(presetName);
                        var line = preset?.WeakLine;
                        if (preset != null && line != null) {
                            return new FilterAssignment(ninaName, preset.Name, line.Line, line.BandwidthNm);
                        }
                    }
                }
            }

            // A single line, but only with its bandwidth in the name.
            var match = BandwidthInName.Match(ninaName);
            var lineKey = SingleLine(ninaName);
            if (lineKey != null && match.Success) {
                var width = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                if (width > 0) {
                    return new FilterAssignment(ninaName, string.Empty, lineKey, width);
                }
            }
            return null;
        }

        // A line name standing on its own: not inside another word, digits may follow
        // ("Ha3nm"). Only one line may match, or the name is a duo and says too little.
        private static readonly (string Line, Regex Pattern)[] LineNames = {
            ("Ha", new Regex(@"(^|[^a-z])(h-?alpha|ha|hα)([^a-z]|$)", RegexOptions.IgnoreCase)),
            ("OIII", new Regex(@"(^|[^a-z])(o-?iii|o3)([^a-z]|$)", RegexOptions.IgnoreCase)),
            ("SII", new Regex(@"(^|[^a-z])(s-?ii|s2)([^a-z]|$)", RegexOptions.IgnoreCase)),
            ("Hb", new Regex(@"(^|[^a-z])(h-?beta|hb|hβ)([^a-z]|$)", RegexOptions.IgnoreCase)),
        };

        private static string? SingleLine(string ninaName) {
            var lines = LineNames.Where(l => l.Pattern.IsMatch(ninaName)).Select(l => l.Line).ToList();
            return lines.Count == 1 ? lines[0] : null;
        }

        /// <summary>
        /// The one line a filter's name names ("S2" → SII, "OIII 6nm" → OIII), or null.
        /// Unlike <see cref="Suggest"/> it needs no bandwidth: it picks the line within a
        /// filter set the user chose, it does not invent a filter.
        /// </summary>
        public static string? LineInName(string? ninaName) =>
            string.IsNullOrWhiteSpace(ninaName) ? null : SingleLine(ninaName);

        // ------------------------------------------------------------------
        // storage: a JSON array in the plugin settings of the profile
        // ------------------------------------------------------------------

        public static string Serialize(IEnumerable<FilterAssignment> assignments) =>
            JsonSerializer.Serialize(assignments.Where(IsValid).ToList());

        /// <summary>Unreadable or invalid entries are dropped, never guessed at.</summary>
        public static IReadOnlyList<FilterAssignment> Deserialize(string? json) {
            if (string.IsNullOrWhiteSpace(json)) {
                return Array.Empty<FilterAssignment>();
            }
            try {
                return (JsonSerializer.Deserialize<List<FilterAssignment>>(json) ?? new List<FilterAssignment>())
                    .Where(IsValid)
                    .ToList();
            } catch (JsonException) {
                return Array.Empty<FilterAssignment>();
            }
        }

        public static bool IsValid(FilterAssignment? a) {
            if (a == null || string.IsNullOrWhiteSpace(a.NinaName) || !LineKeys.Contains(a.Line)) {
                return false;
            }
            if (a.Line != "RGB" && !(a.BandwidthNm > 0)) {
                return false;
            }
            if (a.Preset.Length > 0) {
                var preset = FilterCatalog.Find(a.Preset);
                return preset != null && preset.Find(a.Line) != null;
            }
            return true;
        }
    }
}
