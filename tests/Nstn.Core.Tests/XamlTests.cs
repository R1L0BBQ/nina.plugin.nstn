using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace Nstn.Core.Tests {

    /// <summary>
    /// The plugin's XAML against N.I.N.A.'s theme. These are text checks, the views
    /// themselves only exist inside N.I.N.A.: what they catch is invisible in the
    /// test instance on one theme and unreadable on another.
    /// </summary>
    public class XamlTests {

        private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        private static IEnumerable<string> XamlFiles() =>
            Directory.EnumerateFiles(Path.Combine(TextTests.RepositoryRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        /// <summary>
        /// A TextBlock style of our own replaces N.I.N.A.'s implicit one (StandardTextBlock,
        /// Foreground = PrimaryBrush). Without BasedOn or its own Foreground the text is
        /// black: the 0.8.0 section headings were unreadable on Marc's dark theme.
        /// </summary>
        [Fact]
        public void EveryTextBlockStyleKeepsTheThemeColour() {
            var problems = new List<string>();
            foreach (var file in XamlFiles()) {
                var document = XDocument.Load(file, LoadOptions.SetLineInfo);
                foreach (var style in document.Descendants(Presentation + "Style")) {
                    var target = (string?)style.Attribute("TargetType") ?? string.Empty;
                    if (target != "TextBlock" && target != "{x:Type TextBlock}") {
                        continue;
                    }
                    var hasColour = style.Attribute("BasedOn") != null
                        || style.Elements(Presentation + "Setter").Any(s => (string?)s.Attribute("Property") == "Foreground");
                    if (!hasColour) {
                        problems.Add($"{Path.GetFileName(file)}:{((IXmlLineInfo)style).LineNumber}");
                    }
                }
            }
            Assert.True(problems.Count == 0, "TextBlock style without BasedOn or Foreground: " + string.Join(", ", problems));
        }

        /// <summary>
        /// An element spanning fixed columns and an Auto column: if it is wider than they
        /// are, WPF spreads the excess over all of them, the fixed ones too, and whatever sits
        /// in a fixed column moves. Night Info's bars ran 10 px (20 min) ahead of the curves
        /// and the hour axis. The Auto column must share its width with the spanning
        /// element's own (SharedSizeGroup), so the excess goes there.
        /// </summary>
        [Fact]
        public void ASpanOverFixedColumnsKeepsThemFixed() {
            var problems = new List<string>();
            var spans = 0;
            foreach (var file in XamlFiles()) {
                var document = XDocument.Load(file, LoadOptions.SetLineInfo);
                foreach (var grid in document.Descendants(Presentation + "Grid")) {
                    var columns = grid.Element(Presentation + "Grid.ColumnDefinitions")?.Elements(Presentation + "ColumnDefinition").ToList();
                    if (columns == null) {
                        continue;
                    }
                    foreach (var child in grid.Elements().Where(e => e.Attribute("Grid.ColumnSpan") != null)) {
                        spans++;
                        var first = int.TryParse((string?)child.Attribute("Grid.Column"), out var c) ? c : 0;
                        var spanned = columns.Skip(first).Take(int.Parse((string)child.Attribute("Grid.ColumnSpan")!)).ToList();
                        var hasFixed = spanned.Any(d => double.TryParse((string?)d.Attribute("Width"), out _));
                        // A group helps only if the spanning element's own column is in it and a scope holds both.
                        var inScope = grid.AncestorsAndSelf().Any(e => (string?)e.Attribute("Grid.IsSharedSizeScope") == "True");
                        var loose = spanned.Where(d => (string?)d.Attribute("Width") == "Auto").Any(d =>
                            d.Attribute("SharedSizeGroup") is not XAttribute group || !inScope
                            || !child.Descendants(Presentation + "ColumnDefinition").Any(inner => (string?)inner.Attribute("SharedSizeGroup") == group.Value));
                        if (hasFixed && loose) {
                            problems.Add($"{Path.GetFileName(file)}:{((IXmlLineInfo)child).LineNumber}");
                        }
                    }
                }
            }
            Assert.True(spans > 0, "no element with Grid.ColumnSpan found - is the source tree there?");
            Assert.True(problems.Count == 0, "Span over fixed columns and an Auto column without SharedSizeGroup: " + string.Join(", ", problems));
        }

        /// <summary>Reach of the check above: it has to see the styles it is about.</summary>
        [Fact]
        public void TheCheckSeesThePlanStyles() {
            var plan = XamlFiles().Single(f => Path.GetFileName(f) == "FilterPlanTemplates.xaml");
            var keys = XDocument.Load(plan).Descendants(Presentation + "Style")
                .Where(s => (string?)s.Attribute("TargetType") == "TextBlock")
                .Select(s => (string?)s.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key"))
                .ToList();
            Assert.Contains("NstnPlanHeading", keys);
            Assert.Contains("NstnPlanSecondary", keys);
        }
    }
}
