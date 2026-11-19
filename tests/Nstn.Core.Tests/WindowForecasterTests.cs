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

        /// <summary>
        /// At 08:58 on the 19th the plan showed "tonight (18 Nov)" and planned 0 s: until
        /// noon the night before counted, although it was over. It now ends at sunrise.
        /// </summary>
        [Fact]
        public void AtSunriseTheComingNightIsMeant() {
            const double lat = 49.8, lon = 6.6;   // Kordel
            var night = new DateOnly(2026, 11, 18);
            var forecaster = new WindowForecaster();
            var sunrise = forecaster.SunriseEnding(night, Berlin, lat, lon);
            Assert.NotNull(sunrise);

            // The grid sunrise agrees with the astronomical one (upper limb, refraction) within
            // a grid step, give or take the half minute two refraction models differ by.
            var bundle = forecaster.Night(new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc), Berlin, lat, lon, 23.46, 30.66, 21.0);
            var exact = NightInfoBuilder.Build(bundle, Array.Empty<EvalFilter>()).EventMs(NightEventKind.Sunrise);
            Assert.NotNull(exact);
            Assert.InRange(sunrise!.Value - exact!.Value, -30_000, NightModel.SampleStepMinutes * 60_000.0 + 30_000);

            DateOnly At(double epochMs) => forecaster.NightFor(NightModel.ToUtcDateTime(epochMs), Berlin, lat, lon);
            Assert.Equal(night, At(sunrise.Value - 60_000));               // dawn: still the night before
            Assert.Equal(night.AddDays(1), At(sunrise.Value));             // sun up: the coming night
            Assert.Equal(night.AddDays(1), At(NightModel.ToEpochMs(new DateTime(2026, 11, 19, 7, 58, 0, DateTimeKind.Utc))));   // 08:58
            Assert.Equal(night, At(NightModel.ToEpochMs(new DateTime(2026, 11, 18, 23, 0, 0, DateTimeKind.Utc))));              // midnight
            Assert.Equal(night, At(NightModel.ToEpochMs(new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc))));              // afternoon before

            // The forecast follows: in the morning it is the coming night's, the sun counts as up.
            var morning = new DateTime(2026, 11, 19, 7, 58, 0, DateTimeKind.Utc);
            var forecast = forecaster.Forecast(morning, Berlin, lat, lon, 23.46, 30.66, 21.0, NightModel.LineFilter("Ha", 7), NightModel.DefaultSettings);
            Assert.Equal(night.AddDays(1), forecast.NightDate);
            Assert.True(forecast.NextChangeAfter(NightModel.ToEpochMs(morning)) != null, "the coming night's window opens later");
            var afternoon = NightModel.ToEpochMs(new DateTime(2026, 11, 19, 13, 0, 0, DateTimeKind.Utc));
            Assert.True(forecast.SunUpAt(afternoon));
            Assert.False(forecast.SunUpAt(NightModel.ToEpochMs(new DateTime(2026, 11, 19, 23, 0, 0, DateTimeKind.Utc))));
        }

        /// <summary>Without a sunrise (polar night or day) the noon rule stays.</summary>
        [Theory]
        [InlineData(2026, 12, 15)]   // polar night: the sun never rises
        [InlineData(2026, 6, 15)]    // midnight sun: it never sets
        public void WithoutASunriseTheNoonRuleStays(int year, int month, int day) {
            const double lat = 78.2, lon = 15.6;   // Longyearbyen
            var night = new DateOnly(year, month, day);
            var forecaster = new WindowForecaster();
            Assert.Null(forecaster.SunriseEnding(night, Berlin, lat, lon));
            var tenInTheMorning = new DateTime(year, month, day + 1, 8, 0, 0, DateTimeKind.Utc);
            Assert.Equal(night, forecaster.NightFor(tenInTheMorning, Berlin, lat, lon));
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
