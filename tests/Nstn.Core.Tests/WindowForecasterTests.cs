using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    public class WindowForecasterTests {

        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        private static JsonElement GoldenCase(string name) {
            var path = Path.Combine(AppContext.BaseDirectory, "golden", "night_model.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("cases").EnumerateArray()
                .Single(c => c.GetProperty("name").GetString() == name).Clone();
        }

        [Theory]
        [InlineData("2025-09-18T10:30:00Z", "2025-09-18")]  // 12:30 CEST - already the evening's night
        [InlineData("2025-09-18T09:59:00Z", "2025-09-17")]  // 11:59 CEST - still last night
        [InlineData("2025-09-18T10:00:00Z", "2025-09-18")]  // 12:00 CEST - the new night starts
        [InlineData("2025-09-18T22:00:00Z", "2025-09-18")]  // midnight
        [InlineData("2025-09-19T02:00:00Z", "2025-09-18")]  // 04:00 CEST - same night
        public void NightDateFollowsLocalNoon(string utc, string night) {
            var moment = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            Assert.Equal(DateOnly.Parse(night), WindowForecaster.NightDateFor(moment, Berlin));
        }

        /// <summary>
        /// The forecaster must hand out exactly the golden windows: every case's
        /// segments, asked for at a moment inside that night.
        /// </summary>
        [Theory]
        [InlineData("mid-north-autumn")]
        [InlineData("mid-north-newmoon")]
        [InlineData("circumpolar")]
        public void ForecastReproducesTheGoldenWindows(string name) {
            var c = GoldenCase(name);
            var date = DateOnly.Parse(c.GetProperty("date").GetString()!);
            var lateEvening = new DateTime(date.Year, date.Month, date.Day, 20, 0, 0, DateTimeKind.Utc);
            var forecaster = new WindowForecaster();
            foreach (var window in c.GetProperty("windows").EnumerateArray()) {
                var band = window.GetProperty("broadband").GetBoolean()
                    ? NightModel.BroadbandFilter(window.GetProperty("bw").GetDouble())
                    : NightModel.LineFilter(window.GetProperty("key").GetString()!, window.GetProperty("bw").GetDouble());
                var forecast = forecaster.Forecast(lateEvening, Berlin,
                    c.GetProperty("lat").GetDouble(), c.GetProperty("lon").GetDouble(),
                    c.GetProperty("ra").GetDouble(), c.GetProperty("dec").GetDouble(),
                    c.GetProperty("sqm").GetDouble(), band, NightModel.DefaultSettings);
                var want = window.GetProperty("segs").EnumerateArray().ToList();
                Assert.Equal(date, forecast.NightDate);
                Assert.Equal(want.Count, forecast.Segments.Count);
                for (var i = 0; i < want.Count; i++) {
                    Assert.True(Math.Abs(want[i][0].GetDouble() - forecast.Segments[i].Start) < 1e-3);
                    Assert.True(Math.Abs(want[i][1].GetDouble() - forecast.Segments[i].End) < 1e-3);
                }
            }
            // One sky per night, target and site, however many bands are asked for.
            Assert.Equal(1, forecaster.Computations);
        }

        /// <summary>
        /// The shown sky value must agree with the shown state right before and
        /// after an opening, not only on the grid points.
        /// </summary>
        [Fact]
        public void TheShownSkyValueAgreesWithTheStateAroundAnOpening() {
            var c = GoldenCase("mid-north-autumn");
            var date = DateOnly.Parse(c.GetProperty("date").GetString()!);
            var band = NightModel.LineFilter("Ha", 6);
            var forecast = new WindowForecaster().Forecast(
                new DateTime(date.Year, date.Month, date.Day, 20, 0, 0, DateTimeKind.Utc), Berlin,
                c.GetProperty("lat").GetDouble(), c.GetProperty("lon").GetDouble(),
                c.GetProperty("ra").GetDouble(), c.GetProperty("dec").GetDouble(),
                c.GetProperty("sqm").GetDouble(), band, NightModel.DefaultSettings);
            var opening = forecast.Segments[0].Start;
            foreach (var offsetMs in new[] { -60_000.0, -1.0, 0.0, 1.0, 60_000.0 }) {
                var moment = opening + offsetMs;
                // The full rule - altitude and sky - since a window can also open
                // because the target climbs above the minimum altitude.
                var passes = NightModel.IsWorthwhile(forecast.SampleAt(moment)!, band);
                Assert.True(forecast.IsOpenAt(moment) == passes, $"offset {offsetMs} ms: open {forecast.IsOpenAt(moment)}, sample passes {passes}");
            }
        }

        [Fact]
        public void OpenStateAndNextChangeFollowTheSegments() {
            var band = NightModel.LineFilter("Ha", 6);
            var forecast = new BandForecast(new DateOnly(2025, 9, 18), band,
                new[] { new Interval(1000, 2000), new Interval(5000, 6000) },
                23.1, 20.17, 21.0, new SkyResult(Array.Empty<SkySample>(), 21.0));

            Assert.False(forecast.IsOpenAt(999));
            Assert.Equal(1000, forecast.NextChangeAfter(999));
            Assert.True(forecast.IsOpenAt(1000));
            Assert.Equal(2000, forecast.NextChangeAfter(1500));
            Assert.False(forecast.IsOpenAt(2000));          // half open: the end is closed
            Assert.Equal(5000, forecast.NextChangeAfter(2000));
            Assert.Null(forecast.NextChangeAfter(6000));
            Assert.Equal(2000.0 / 60_000, forecast.OpenMinutes, 12);
        }

        [Fact]
        public void ABrightSiteCanNeverOpenANarrowBand() {
            var oiii = NightModel.LineFilter("OIII", 6);
            var forecast = new BandForecast(new DateOnly(2025, 9, 18), oiii, Array.Empty<Interval>(),
                23.1, NightModel.MinimumSkyBrightness(oiii), 19.5, new SkyResult(Array.Empty<SkySample>(), 19.5));
            Assert.True(forecast.NeverOpensAtSite);
        }
    }
}
