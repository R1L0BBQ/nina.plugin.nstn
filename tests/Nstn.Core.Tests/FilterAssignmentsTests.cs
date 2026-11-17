using System.Linq;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    public class FilterAssignmentsTests {

        [Theory]
        [InlineData("Askar D1", "Askar D1 - H-Alpha & O-III", "OIII", 6)]
        [InlineData("askar_d1", "Askar D1 - H-Alpha & O-III", "OIII", 6)]
        [InlineData("L-eXtreme", "Optolong L-eXtreme (7 nm)", "OIII", 7)]
        [InlineData("SV220 7nm", "SVBONY SV220 (7 nm)", "OIII", 7)]
        [InlineData("Askar C2", "Askar C2 - S-II & O-III", "OIII", 35)]
        public void KnownProductsAreSuggestedOnTheirWeakLine(string name, string preset, string line, double width) {
            var suggestion = FilterAssignments.Suggest(name);
            Assert.NotNull(suggestion);
            Assert.Equal(preset, suggestion!.Preset);
            Assert.Equal(line, suggestion.Line);
            Assert.Equal(width, suggestion.BandwidthNm);
        }

        [Theory]
        [InlineData("L")]
        [InlineData("Lum")]
        [InlineData("Red")]
        [InlineData("kein_Filter")]
        [InlineData("NoFilter")]
        [InlineData("UV/IR")]
        public void BroadbandNamesAreBroadband(string name) {
            var suggestion = FilterAssignments.Suggest(name)!;
            Assert.True(suggestion.IsBroadband);
            Assert.Equal("", suggestion.Preset);
        }

        [Theory]
        [InlineData("Ha 3nm", "Ha", 3)]
        [InlineData("Ha3nm", "Ha", 3)]
        [InlineData("OIII 6,5 nm", "OIII", 6.5)]
        [InlineData("H-alpha 7 nm", "Ha", 7)]
        [InlineData("S2 4.5nm", "SII", 4.5)]
        public void ASingleLineWithItsBandwidthIsTakenAsIs(string name, string line, double width) {
            var suggestion = FilterAssignments.Suggest(name)!;
            Assert.Equal("", suggestion.Preset);
            Assert.Equal(line, suggestion.Line);
            Assert.Equal(width, suggestion.BandwidthNm);
        }

        /// <summary>No suggestion beats a wrong one: a default width would move the window silently.</summary>
        [Theory]
        [InlineData("Ha")]                  // no bandwidth
        [InlineData("OIII")]
        [InlineData("Askar Super D1")]      // 8.5/6.5 nm, not the D1
        [InlineData("ALP-T 5nm")]           // H-alpha/O III or S II/H-beta?
        [InlineData("Ha OIII 7nm")]         // two lines, no product
        [InlineData("Shark")]                // "ha" inside a word
        [InlineData("")]
        public void AmbiguousNamesGetNoSuggestion(string name) {
            Assert.Null(FilterAssignments.Suggest(name));
        }

        [Fact]
        public void AssignmentsSurviveTheRoundTripAndInvalidOnesAreDropped() {
            var list = new[] {
                new FilterAssignment("Askar D1", "Askar D1 - H-Alpha & O-III", "Ha", 6),
                new FilterAssignment("L", "", "RGB", 89),
                new FilterAssignment("Broken", "Askar D1 - H-Alpha & O-III", "SII", 6),  // D1 has no S II
                new FilterAssignment("Zero", "", "Ha", 0),
            };
            var restored = FilterAssignments.Deserialize(FilterAssignments.Serialize(list));
            Assert.Equal(list.Take(2), restored);
            Assert.Empty(FilterAssignments.Deserialize("not json"));
            Assert.Empty(FilterAssignments.Deserialize(null));
        }

        /// <summary>
        /// Only the choice is stored. 0.4.0 also wrote the derived IsBroadband and Band
        /// (with the Rayleigh weight) into the profile; such entries must still load.
        /// </summary>
        [Fact]
        public void OnlyTheChoiceIsStoredAndOlderEntriesStillLoad() {
            var json = FilterAssignments.Serialize(new[] { new FilterAssignment("Ha", "", "Ha", 7) });
            Assert.Equal("[{\"NinaName\":\"Ha\",\"Preset\":\"\",\"Line\":\"Ha\",\"BandwidthNm\":7}]", json);

            const string written040 = "[{\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89,\"IsBroadband\":true,"
                + "\"Band\":{\"Key\":\"RGB\",\"Label\":\"RGB\",\"Broadband\":true,\"WaveF\":1,\"BandwidthNm\":89}}]";
            Assert.Equal(new[] { new FilterAssignment("L", "", "RGB", 89) }, FilterAssignments.Deserialize(written040));
        }

        [Fact]
        public void LookupIgnoresCaseAndSurroundingSpaces() {
            var list = new[] { new FilterAssignment("Askar D1", "Askar D1 - H-Alpha & O-III", "OIII", 6) };
            Assert.NotNull(FilterAssignments.Find(list, " askar d1 "));
            Assert.Null(FilterAssignments.Find(list, "Askar D2"));
            Assert.Null(FilterAssignments.Find(list, null));
        }

        [Fact]
        public void TheBandFollowsTheAssignment() {
            Assert.True(new FilterAssignment("L", "", "RGB", 89).Band.Broadband);
            var oiii = new FilterAssignment("D1", "Askar D1 - H-Alpha & O-III", "OIII", 6).Band;
            Assert.Equal("OIII", oiii.Key);
            Assert.Equal(6, oiii.BandwidthNm);
        }
    }
}
