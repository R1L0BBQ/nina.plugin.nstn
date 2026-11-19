using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    /// <summary>
    /// The string tables against each other and against the code. A text that is
    /// missing in a table only shows up in front of a user in that language, so the
    /// tables are checked here, with the source as the list of what is needed.
    /// </summary>
    // Text.Culture is global: no test of this class runs in parallel with another one that formats texts.
    [Collection(TextCollection.Name)]
    public class TextTests {

        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?::[^}]*)?\}");
        private static readonly Regex Token = new Regex(@"\{(t|nina):([A-Za-z0-9_]+)\}");

        // Keys used in the code: Text.T("Key"…) in C#, {nstn:T Key} in XAML.
        private static readonly Regex CodeKey = new Regex(@"Text\.T\(\s*""([A-Za-z0-9_]+)""");
        private static readonly Regex XamlKey = new Regex(@"\{nstn:T\s+([A-Za-z0-9_]+)\s*\}");

        internal static string RepositoryRoot() {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Nstn.Core"))) {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found above " + AppContext.BaseDirectory);
        }

        private static IReadOnlyCollection<string> KeysUsedInCode() {
            var source = Path.Combine(RepositoryRoot(), "src");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)) {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) {
                    continue;
                }
                var regex = file.EndsWith(".cs", StringComparison.Ordinal) ? CodeKey
                    : file.EndsWith(".xaml", StringComparison.Ordinal) ? XamlKey
                    : null;
                if (regex == null) {
                    continue;
                }
                foreach (Match match in regex.Matches(File.ReadAllText(file))) {
                    keys.Add(match.Groups[1].Value);
                }
            }
            return keys;
        }

        private static IReadOnlyDictionary<string, string> English => Text.TableOf(Text.FallbackLanguage);

        [Fact]
        public void EnglishAndGermanAreThere() {
            Assert.Contains("en", Text.Languages);
            Assert.Contains("de", Text.Languages);
        }

        /// <summary>
        /// A key written twice is silently read once: a new label "Plan_Priority" hid the
        /// night plan's sentence of the same name, and the label showed that sentence.
        /// </summary>
        [Fact]
        public void NoKeyIsWrittenTwice() {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "Nstn.Core", "Strings"), "*.json")) {
                var reader = new System.Text.Json.Utf8JsonReader(File.ReadAllBytes(file));
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var twice = new List<string>();
                while (reader.Read()) {
                    if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName && reader.CurrentDepth == 1 && !seen.Add(reader.GetString()!)) {
                        twice.Add(reader.GetString()!);
                    }
                }
                Assert.True(twice.Count == 0, $"{Path.GetFileName(file)}: {string.Join(", ", twice)}");
            }
        }

        [Fact]
        public void EveryLanguageHasExactlyTheEnglishKeys() {
            foreach (var language in Text.Languages) {
                var table = Text.TableOf(language);
                Assert.Empty(English.Keys.Except(table.Keys));
                Assert.Empty(table.Keys.Except(English.Keys));
                Assert.All(table, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{language}: {entry.Key} is empty"));
            }
        }

        /// <summary>A translation that drops {1} loses a value; one that adds {3} throws at runtime.</summary>
        [Fact]
        public void TranslationsUseTheSamePlaceholdersAndTokens() {
            foreach (var language in Text.Languages) {
                foreach (var (key, english) in English) {
                    var translated = Text.TableOf(language)[key];
                    string Shape(string text, Regex regex) =>
                        string.Join(",", regex.Matches(text).Select(m => m.Groups[1].Value + (m.Groups.Count > 2 ? ":" + m.Groups[2].Value : "")).Distinct().OrderBy(s => s, StringComparer.Ordinal));
                    Assert.True(Shape(english, Placeholder) == Shape(translated, Placeholder), $"{language}: placeholders of {key} differ");
                    Assert.True(Shape(english, Token) == Shape(translated, Token), $"{language}: tokens of {key} differ");
                }
            }
        }

        [Fact]
        public void EveryTokenPointsAtSomethingThatExists() {
            foreach (var language in Text.Languages) {
                foreach (var (key, text) in Text.TableOf(language)) {
                    foreach (Match match in Token.Matches(text)) {
                        var name = match.Groups[2].Value;
                        Assert.True(match.Groups[1].Value == "t" ? English.ContainsKey(name) : Text.NinaEnglishNames.ContainsKey(name),
                            $"{language}: {key} refers to unknown {match.Value}");
                    }
                }
            }
        }

        /// <summary>
        /// Every key the code asks for exists, and every entry is asked for. Keys are
        /// only ever written as literals, so reading the source finds all of them.
        /// </summary>
        [Fact]
        public void TheTablesMatchTheCode() {
            var used = KeysUsedInCode();
            var referenced = English.Values.SelectMany(v => Token.Matches(v)).Where(m => m.Groups[1].Value == "t").Select(m => m.Groups[2].Value);
            Assert.True(used.Count > 100, $"only {used.Count} keys found in the code - is the source tree there?");
            Assert.Empty(used.Except(English.Keys));
            Assert.Empty(English.Keys.Except(used).Except(referenced));
        }

        /// <summary>
        /// The plugin description carries the English guide as constants
        /// (SequenceGuide.cs, generated from en.json); it must say what the
        /// sequencer says to an English user.
        /// </summary>
        [Theory]
        [InlineData("Easy", "Guide_Easy")]
        [InlineData("Tree", "Guide_Tree")]
        [InlineData("Rules", "Guide_Rules")]
        public void ThePluginDescriptionCarriesTheEnglishGuide(string constant, string key) {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NINA.Plugin.Nstn", "SequenceGuide.cs"));
            var match = Regex.Match(source, constant + @" = @""((?:[^""]|"""")*)"";", RegexOptions.Singleline);
            Assert.True(match.Success, $"{constant} not found in SequenceGuide.cs");
            var before = Text.NinaName;
            try {
                Text.NinaName = null;
                using var _ = new LanguageScope("en-US");
                Assert.Equal(Text.T(key), match.Groups[1].Value.Replace("\"\"", "\"").Replace("\r\n", "\n"));
            } finally {
                Text.NinaName = before;
            }
        }

        [Fact]
        public void EveryEntryFormats() {
            var arguments = Enumerable.Range(0, 6).Select(i => (object)(i + 0.5)).ToArray();
            foreach (var language in Text.Languages) {
                using var _ = new LanguageScope(language);
                foreach (var key in English.Keys) {
                    var text = Text.T(key, arguments);
                    Assert.DoesNotContain("{t:", text);
                    Assert.DoesNotContain("{nina:", text);
                }
            }
        }

        [Fact]
        public void UnknownLanguagesFallBackToEnglishAndRegionsToTheirLanguage() {
            using (new LanguageScope("fr-FR")) {
                Assert.Equal("Moon distance", Text.T("Chart_MoonDistance"));
            }
            using (new LanguageScope("de-AT")) {
                Assert.Equal("Mondabstand", Text.T("Chart_MoonDistance"));
            }
            using (new LanguageScope("de-DE")) {
                Assert.Equal("Mondabstand", Text.T("Chart_MoonDistance"));
            }
        }

        [Fact]
        public void NumbersAndDatesFollowTheLanguage() {
            using (new LanguageScope("de-DE")) {
                Assert.Equal("Himmel in Zielrichtung 20,7 mag/arcsec²", Text.T("Tip_Sky", 20.71));
                Assert.Equal("17. Nov.", Text.Date(new DateOnly(2026, 11, 17)));
                Assert.Equal("7 h 05 min", Text.Duration(425));
                Assert.Equal("„M 31“", Text.Quote("M 31"));
            }
            using (new LanguageScope("en-GB")) {
                Assert.Equal("Sky towards the target 20.7 mag/arcsec²", Text.T("Tip_Sky", 20.71));
                Assert.Equal("17 Nov", Text.Date(new DateOnly(2026, 11, 17)));
                Assert.Equal("42 min", Text.Duration(42));
                Assert.Equal("'M 31'", Text.Quote("M 31"));
            }
        }

        [Fact]
        public void NinaNamesComeFromNinaWhenItAnswers() {
            var before = Text.NinaName;
            try {
                using var _ = new LanguageScope("de-DE");
                Text.NinaName = null;
                Assert.Contains("„Loop For Iterations“", Text.T("Plan_Priority", "B"));
                Text.NinaName = token => token == "LoopForIterations" ? "Schleife für Iterationen" : null;
                Assert.Contains("„Schleife für Iterationen“", Text.T("Plan_Priority", "B"));
                // Unanswered tokens keep the English name rather than showing the token.
                Assert.Contains("Smart Exposure", Text.T("Cond_IssueBlockNoFilter"));
            } finally {
                Text.NinaName = before;
            }
        }

        [Fact]
        public void AMissingKeyShowsTheKey() {
            Assert.Equal("No_Such_Key", Text.T("No_Such_Key"));
        }

        [Fact]
        public void ChangingTheLanguageIsAnnounced() {
            var raised = 0;
            void Count(object? sender, EventArgs e) => raised++;
            Text.Changed += Count;
            try {
                using (new LanguageScope("de-DE")) {
                    Text.Culture = CultureInfo.GetCultureInfo("de-DE");   // unchanged: no event
                }
            } finally {
                Text.Changed -= Count;
            }
            Assert.Equal(2, raised);   // to German and back
        }
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public class TextCollection {
        public const string Name = "Text culture";
    }

    /// <summary>Switches the text language for one test and back.</summary>
    internal sealed class LanguageScope : IDisposable {
        private readonly CultureInfo before = Text.Culture;

        public LanguageScope(string culture) => Text.Culture = CultureInfo.GetCultureInfo(culture);

        public void Dispose() => Text.Culture = before;
    }
}
