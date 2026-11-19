// Everything the plugin says to the user, in the user's language.
//
// One table per language (Strings/<language>.json, embedded), looked up by key.
// The tables live in the assembly itself rather than in satellite assemblies, so
// the plugin stays three DLLs in one folder. A missing language falls back to
// English, a missing key shows the key - the tests make sure neither happens.
//
// Two kinds of tokens are resolved before formatting:
//   {t:Key}     another entry of the same table, e.g. the name of an NSTN instruction;
//   {nina:Item} the name N.I.N.A. itself shows for one of its instructions, in
//               N.I.N.A.'s language. The plugin supplies it (NinaName); without
//               one - in the tests - the English name is used.
// Log messages stay English; they are read by whoever helps with a problem.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nstn.Core {

    public static class Text {

        public const string FallbackLanguage = "en";

        /// <summary>N.I.N.A.'s English names of the instructions the texts refer to.</summary>
        public static IReadOnlyDictionary<string, string> NinaEnglishNames { get; } = new Dictionary<string, string> {
            ["LoopForIterations"] = "Loop For Iterations",
            ["LoopUntilTime"] = "Loop Until Time",
            ["SmartExposure"] = "Smart Exposure",
            ["TakeExposure"] = "Take Exposure",
            ["SwitchFilter"] = "Switch Filter",
            ["MeridianFlip"] = "Meridian Flip",
            ["AutofocusAfterFilterChange"] = "AF After Filter Change",
            ["DitherAfterExposures"] = "Dither after Exposures",
            ["SequentialSet"] = "Sequential Instruction Set",
            ["TargetSet"] = "Deep Sky Object Instruction Set",
            ["SlewAndCenter"] = "Slew and center",
            ["StartGuiding"] = "Start Guiding",
            ["Options"] = "Options",
            ["General"] = "General",
            ["Astrometry"] = "Astrometry",
            ["Equipment"] = "Equipment",
            ["FilterWheel"] = "Filterwheel",
            ["Plugins"] = "Plugins",
        };

        private static readonly Regex Token = new Regex(@"\{(t|nina):([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tables = Load();

        private static volatile CultureInfo culture = CultureInfo.GetCultureInfo(FallbackLanguage);

        /// <summary>Raised after <see cref="Culture"/> changed, on the thread that changed it.</summary>
        public static event EventHandler? Changed;

        /// <summary>The user's language; also formats numbers and dates in the texts.</summary>
        public static CultureInfo Culture {
            get => culture;
            set {
                if (value == null || value.Name == culture.Name) {
                    return;
                }
                culture = value;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Resolves a <c>{nina:Item}</c> token to N.I.N.A.'s own name in its current
        /// language; null or an empty answer falls back to the English name.
        /// </summary>
        public static Func<string, string?>? NinaName { get; set; }

        /// <summary>The languages with a table, as two-letter codes.</summary>
        public static IReadOnlyCollection<string> Languages => Tables.Keys.ToList();

        /// <summary>The raw table of one language, for the tests.</summary>
        public static IReadOnlyDictionary<string, string> TableOf(string language) => Tables[language];

        /// <summary>The text for a key in the current language, tokens resolved, formatted with <paramref name="args"/>.</summary>
        public static string T(string key, params object?[] args) {
            var current = culture;
            var text = Resolve(Lookup(key, current), current);
            return args.Length == 0 ? text : string.Format(current, text, args);
        }

        /// <summary>A date as the language writes it, e.g. "17 Nov" or "17. Nov.".</summary>
        public static string Date(DateOnly date) => date.ToString(Text.T("Format_Date"), culture);

        /// <summary>
        /// A night by both its dates, e.g. "18./19. Nov." or "30 Nov/1 Dec": "tonight
        /// (18 Nov)" read wrong on the morning of the 19th.
        /// </summary>
        public static string Night(DateOnly night) {
            var morning = night.AddDays(1);
            if (morning.Month == night.Month) {
                return Text.T("Format_Night", night, morning);
            }
            return Text.T("Format_NightMonths", night, morning);
        }

        /// <summary>A local clock time; 24 hours in every language N.I.N.A. users observe in.</summary>
        public static string Clock(DateTime localTime) => localTime.ToString("HH:mm", CultureInfo.InvariantCulture);

        /// <summary>A duration such as "7 h 05 min" or "42 min".</summary>
        public static string Duration(double minutes) {
            var total = (int)Math.Round(minutes);
            return total >= 60 ? Text.T("Format_HoursMinutes", total / 60, total % 60) : Text.T("Format_Minutes", total);
        }

        /// <summary>A name the user gave, quoted as the language quotes.</summary>
        public static string Quote(string name) => Text.T("Format_Quote", name);

        private static string Lookup(string key, CultureInfo current) {
            foreach (var language in new[] { current.Name, current.TwoLetterISOLanguageName, FallbackLanguage }) {
                if (Tables.TryGetValue(language, out var table) && table.TryGetValue(key, out var text)) {
                    return text;
                }
            }
            return key;
        }

        private static string Resolve(string text, CultureInfo current) =>
            Token.Replace(text, match => {
                var name = match.Groups[2].Value;
                if (match.Groups[1].Value == "t") {
                    return Lookup(name, current);
                }
                var nina = NinaName?.Invoke(name);
                return !string.IsNullOrEmpty(nina) ? nina
                    : NinaEnglishNames.TryGetValue(name, out var english) ? english
                    : name;
            });

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load() {
            var assembly = typeof(Text).Assembly;
            var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            const string prefix = "Nstn.Core.Strings.";
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))) {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
                    ?? throw new InvalidDataException($"Empty string table {name}");
                tables[name.Substring(prefix.Length, name.Length - prefix.Length - ".json".Length)] = entries;
            }
            return tables;
        }
    }
}
