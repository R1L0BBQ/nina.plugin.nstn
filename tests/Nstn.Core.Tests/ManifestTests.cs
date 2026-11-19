using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Nstn.Core.Tests {

    /// <summary>
    /// The plugin's identity as N.I.N.A. and its plugin repository read it: the assembly
    /// attributes become the manifest (isbeorn/nina.plugin.manifests, manifest.schema.json),
    /// the title becomes the install folder and the key of the options page.
    /// </summary>
    public class ManifestTests {

        private static string AssemblyInfo() =>
            File.ReadAllText(Path.Combine(TextTests.RepositoryRoot(), "src", "NINA.Plugin.Nstn", "Properties", "AssemblyInfo.cs"));

        private static string Attribute(string name) {
            var match = Regex.Match(AssemblyInfo(), $@"\[assembly: {name}\(""([^""]*)""\)\]");
            Assert.True(match.Success, $"{name} not found in AssemblyInfo.cs");
            return match.Groups[1].Value;
        }

        private static string Metadata(string key) {
            var match = Regex.Match(AssemblyInfo(), $@"\[assembly: AssemblyMetadata\(""{key}"", ""([^""]*)""\)\]");
            Assert.True(match.Success, $"AssemblyMetadata {key} not found in AssemblyInfo.cs");
            return match.Groups[1].Value;
        }

        /// <summary>
        /// The plugin manager installs into Plugins\3.0.0\&lt;Name&gt;; the installer uses NSTN.
        /// With two names a user who switches would load the plugin twice.
        /// </summary>
        [Fact]
        public void TheNameIsTheInstallersFolder() {
            Assert.Equal("NSTN", Attribute("AssemblyTitle"));
            var installer = File.ReadAllText(Path.Combine(TextTests.RepositoryRoot(), "installer", "Install-NSTN.ps1"));
            Assert.Contains(@"'NINA\Plugins\3.0.0\NSTN'", installer);
        }

        /// <summary>N.I.N.A. looks the options page up as AssemblyTitle + "_Options"; a renamed title loses it silently.</summary>
        [Fact]
        public void TheOptionsPageKeysOnTheTitle() {
            var options = File.ReadAllText(Path.Combine(TextTests.RepositoryRoot(), "src", "NINA.Plugin.Nstn", "Options.xaml"));
            Assert.Contains($"x:Key=\"{Attribute("AssemblyTitle")}_Options\"", options);
        }

        /// <summary>Limits and required fields of manifest.schema.json.</summary>
        [Fact]
        public void TheAttributesFitTheManifestSchema() {
            Assert.InRange(Attribute("AssemblyTitle").Length, 1, 50);
            Assert.InRange(Attribute("AssemblyDescription").Length, 1, 256);
            Assert.InRange(Attribute("AssemblyCompany").Length, 1, 256);
            Assert.Matches(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", Attribute("Guid"));
            Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", Attribute("AssemblyFileVersion"));
            Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", Metadata("MinimumApplicationVersion"));
            var tags = Metadata("Tags").Split(',');
            Assert.InRange(tags.Length, 1, 20);
            Assert.All(tags, t => Assert.InRange(t.Length, 1, 30));
            Assert.All(tags, t => Assert.Equal(t.Trim(), t));
            Assert.Equal("MPL-2.0", Metadata("License"));
            foreach (var key in new[] { "Repository", "LicenseURL", "Homepage", "ChangelogURL" }) {
                Assert.StartsWith("https://", Metadata(key));
            }
        }

        /// <summary>The repository link is the source notice MPL 3.2 asks for; it has to be the published one.</summary>
        [Fact]
        public void TheRepositoryIsThePublishedOne() {
            Assert.Equal("https://github.com/R1L0BBQ/nina.plugin.nstn", Metadata("Repository"));
            var notices = File.ReadAllText(Path.Combine(TextTests.RepositoryRoot(), "src", "NINA.Plugin.Nstn", "THIRD-PARTY-NOTICES.txt"));
            Assert.Contains(Metadata("Repository"), notices);
            Assert.Contains("MPL-2.0", notices);
            var liesmich = File.ReadAllText(Path.Combine(TextTests.RepositoryRoot(), "installer", "LIESMICH.txt"));
            Assert.Contains(Metadata("Repository"), liesmich);
        }
    }
}
