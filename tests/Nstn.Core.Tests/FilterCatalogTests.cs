using System;
using System.Linq;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    public class FilterCatalogTests {

        [Fact]
        public void BothTablesAreLoaded() {
            Assert.Equal(17, FilterCatalog.All.Count(f => f.Kind == FilterKind.Duo));
            Assert.Equal(12, FilterCatalog.All.Count(f => f.Kind == FilterKind.Set));
            // Duo filters first: the order the condition lists them in.
            Assert.Equal(FilterKind.Duo, FilterCatalog.All.First().Kind);
        }

        [Fact]
        public void AskarD1IsHalphaAndOiiiAtSixNanometres() {
            var d1 = FilterCatalog.Find("askar d1 - h-alpha & o-iii");
            Assert.NotNull(d1);
            Assert.Equal(new[] { new FilterLine("Ha", 6), new FilterLine("OIII", 6) }, d1!.Lines);
        }

        [Fact]
        public void DecimalCommaAndTheHbetaColumnAreRead() {
            var unb = FilterCatalog.Find("Baader Ultra-Narrowband")!;
            Assert.Equal(3.5, unb.Find("Ha")!.BandwidthNm);
            Assert.Equal(4, unb.Find("OIII")!.BandwidthNm);
            var pro = FilterCatalog.Find("Antlia Pro (3 nm)")!;
            Assert.Equal(new[] { "Ha", "OIII", "SII", "Hb" }, pro.Lines.Select(l => l.Line));
        }

        [Theory]
        [InlineData("Askar D1 - H-Alpha & O-III", "OIII")]      // equal widths: the blue line
        [InlineData("Askar C1 - H-Alpha & O-III", "OIII")]      // 15 / 35 nm
        [InlineData("Optolong L-Synergy", "OIII")]               // S II / O III
        [InlineData("Antlia ALP-T - S-II & H-Beta (5 nm)", "Hb")]
        public void TheWeakLineOfADuoIsTheOneCollectingMostScatteredLight(string name, string line) {
            Assert.Equal(line, FilterCatalog.Find(name)!.WeakLine!.Line);
        }

        [Fact]
        public void ASetHasNoWeakLine() {
            Assert.Null(FilterCatalog.Find("ZWO (7 nm)")!.WeakLine);
        }

        [Fact]
        public void UnknownNamesAndEmptyCellsAreHandled() {
            Assert.Null(FilterCatalog.Find("Askar Super D1"));
            Assert.Null(FilterCatalog.Find(""));
            var parsed = FilterCatalog.Parse("Filter;Ha;O3;S2;H-Beta (486,1 nm)\r\nX ;;;;\r\nY;7 nm;;;\r\n", FilterKind.Set);
            Assert.Single(parsed);
            Assert.Equal("Y", parsed[0].Name);
        }

        [Fact]
        public void AnUnknownColumnIsAnErrorNotASilentGap() {
            Assert.Throws<FormatException>(() => FilterCatalog.Parse("Filter;Ha;N2\nZ;7 nm;7 nm\n", FilterKind.Set));
        }
    }
}
