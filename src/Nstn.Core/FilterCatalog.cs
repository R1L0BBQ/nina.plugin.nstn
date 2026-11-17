// The filters NSTN knows by name: the duo filters and narrowband sets of the web
// planner (astro-public/dnb-filter.csv and schmalband-filter.csv, embedded
// byte for byte). A suggestion for band and bandwidth, nothing more - product
// lines change and two filters can share a name (Askar sells a "Colour Magic D1"
// at 6/6 nm and a "Super D1" at 8.5/6.5 nm).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Nstn.Core {

    public enum FilterKind {
        /// <summary>One filter passing two lines at once (dnb-filter.csv).</summary>
        Duo,
        /// <summary>A set of single-line filters (schmalband-filter.csv).</summary>
        Set,
    }

    /// <summary>One emission line a catalog filter passes, with its bandwidth.</summary>
    public sealed record FilterLine(string Line, double BandwidthNm) {
        public EvalFilter Band => NightModel.LineFilter(Line, BandwidthNm);
    }

    public sealed record CatalogFilter(string Name, FilterKind Kind, IReadOnlyList<FilterLine> Lines) {

        public FilterLine? Find(string line) => Lines.FirstOrDefault(l => l.Line == line);

        /// <summary>
        /// The line that closes first: the one collecting the most scattered light,
        /// bandwidth times Rayleigh weight. For a duo filter that is the line a block
        /// should be judged on, or the other channel of its subframes floods
        /// unnoticed. Null for a set - each of its filters has its own block.
        /// </summary>
        public FilterLine? WeakLine =>
            Kind == FilterKind.Duo
                ? Lines.OrderByDescending(l => l.BandwidthNm * l.Band.WaveF).First()
                : null;
    }

    public static class FilterCatalog {

        // Column headers of the CSV files to the night model's line keys.
        private static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["Ha"] = "Ha",
            ["O3"] = "OIII",
            ["S2"] = "SII",
            ["H-Beta (486,1 nm)"] = "Hb",
        };

        private static readonly Lazy<IReadOnlyList<CatalogFilter>> Loaded = new Lazy<IReadOnlyList<CatalogFilter>>(Load);

        /// <summary>Duo filters first, then the sets, each in file order.</summary>
        public static IReadOnlyList<CatalogFilter> All => Loaded.Value;

        public static CatalogFilter? Find(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? null
                : All.FirstOrDefault(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        private static IReadOnlyList<CatalogFilter> Load() =>
            Parse(Resource("dnb-filter.csv"), FilterKind.Duo)
                .Concat(Parse(Resource("schmalband-filter.csv"), FilterKind.Set))
                .ToList();

        private static string Resource(string file) {
            var assembly = typeof(FilterCatalog).Assembly;
            var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + file, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// The planner's format: semicolon separated, a header naming the lines,
        /// one filter per row, bandwidths as "3,5 nm" with a decimal comma, an empty
        /// cell for a line the filter does not pass.
        /// </summary>
        public static IReadOnlyList<CatalogFilter> Parse(string csv, FilterKind kind) {
            var rows = csv.TrimStart('﻿')
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(row => row.Split(';'))
                .ToList();
            if (rows.Count == 0) {
                return Array.Empty<CatalogFilter>();
            }
            var header = rows[0];
            var result = new List<CatalogFilter>();
            foreach (var cells in rows.Skip(1)) {
                var name = cells[0].Trim();
                if (name.Length == 0) {
                    continue;
                }
                var lines = new List<FilterLine>();
                for (var column = 1; column < Math.Min(cells.Length, header.Length); column++) {
                    if (!Columns.TryGetValue(header[column].Trim(), out var line)) {
                        throw new FormatException($"Unknown line column '{header[column]}' in the filter catalog.");
                    }
                    var width = Bandwidth(cells[column]);
                    if (width != null) {
                        lines.Add(new FilterLine(line, width.Value));
                    }
                }
                if (lines.Count > 0) {
                    result.Add(new CatalogFilter(name, kind, lines));
                }
            }
            return result;
        }

        private static double? Bandwidth(string cell) {
            var text = cell.Trim();
            if (text.Length == 0) {
                return null;
            }
            if (text.EndsWith("nm", StringComparison.OrdinalIgnoreCase)) {
                text = text.Substring(0, text.Length - 2).Trim();
            }
            return double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
