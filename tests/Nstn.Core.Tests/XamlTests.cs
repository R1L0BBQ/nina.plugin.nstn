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
