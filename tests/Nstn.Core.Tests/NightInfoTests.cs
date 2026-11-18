using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    public class NightInfoTests {

        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        private static JsonElement GoldenCase(string name) {
            var path = Path.Combine(AppContext.BaseDirectory, "golden", "night_model.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("cases").EnumerateArray()
                .Single(c => c.GetProperty("name").GetString() == name).Clone();
        }

        private static NightBundle Bundle(double lat, double lon, DateOnly date, double ra, double dec, double sqm) =>
            new WindowForecaster().Night(new DateTime(date.Year, date.Month, date.Day, 20, 0, 0, DateTimeKind.Utc),
                Berlin, lat, lon, ra, dec, sqm);

        private static NightBundle Bundle(JsonElement c) =>
            Bundle(c.GetProperty("lat").GetDouble(), c.GetProperty("lon").GetDouble(),
                DateOnly.Parse(c.GetProperty("date").GetString()!),
                c.GetProperty("ra").GetDouble(), c.GetProperty("dec").GetDouble(), c.GetProperty("sqm").GetDouble());

        private static NightInfo Info(string name, params EvalFilter[] bands) =>
            NightInfoBuilder.Build(Bundle(GoldenCase(name)), bands);

        private static double? Golden(JsonElement night, string key) =>
            night.GetProperty(key).ValueKind == JsonValueKind.Null ? null : night.GetProperty(key).GetDouble();

        private static double SunAltitudeAt(NightInfo info, double ms) {
            var times = info.Bundle.Night.Times;
            var sun = info.Bundle.Night.SunAltitude;
            var i = (int)Math.Floor((ms - times[0]) / (NightModel.SampleStepMinutes * 60_000.0));
            var f = (ms - times[i]) / (times[i + 1] - times[i]);
            return sun[i] + f * (sun[i + 1] - sun[i]);
        }

        /// <summary>Twilight and moon events are the model's own, unchanged - the golden values.</summary>
        [Theory]
        [InlineData("mid-north-autumn")]
        [InlineData("mid-north-summer")]
        [InlineData("high-north-summer")]
        [InlineData("equator")]
        [InlineData("south-namibia")]
        public void TwilightAndMoonEventsAreTheGoldenOnes(string name) {
            var night = GoldenCase(name).GetProperty("night");
            var info = Info(name);
            void Check(NightEventKind kind, string key) {
                var want = Golden(night, key);
                var got = info.EventMs(kind);
                if (want == null) {
                    Assert.Null(got);
                } else {
                    Assert.NotNull(got);
                    Assert.True(Math.Abs(want.Value - got!.Value) < 1e-3, $"{kind}: {got} vs {want}");
                }
            }
            Check(NightEventKind.NauticalDusk, "astBeginEve");
            Check(NightEventKind.AstronomicalDusk, "nightBeginEve");
            Check(NightEventKind.AstronomicalDawn, "nightEndMorn");
            Check(NightEventKind.NauticalDawn, "astEndMorn");
            Check(NightEventKind.Moonrise, "moonrise");
            Check(NightEventKind.Moonset, "moonset");
        }

        /// <summary>
        /// Sunset and sunrise come from a rise/set search, not from the grid. At
        /// both the sun's refracted centre must sit just below the horizon (half
        /// its diameter), and the evening must run in the right order.
        /// </summary>
        [Theory]
        [InlineData("mid-north-autumn")]
        [InlineData("mid-north-winter")]
        [InlineData("high-north-summer")]
        [InlineData("equator")]
        [InlineData("south-namibia")]
        public void SunsetAndSunriseFrameTheTwilight(string name) {
            var info = Info(name);
            var sunset = info.EventMs(NightEventKind.Sunset)!.Value;
            var sunrise = info.EventMs(NightEventKind.Sunrise)!.Value;
            Assert.InRange(SunAltitudeAt(info, sunset), -0.6, 0.0);
            Assert.InRange(SunAltitudeAt(info, sunrise), -0.6, 0.0);

            var order = new[] {
                NightEventKind.Sunset, NightEventKind.CivilDusk, NightEventKind.NauticalDusk, NightEventKind.AstronomicalDusk,
                NightEventKind.AstronomicalDawn, NightEventKind.NauticalDawn, NightEventKind.CivilDawn, NightEventKind.Sunrise,
            }.Select(info.EventMs).Where(ms => ms != null).Select(ms => ms!.Value).ToList();
            Assert.Equal(order.OrderBy(ms => ms), order);
            // At 60.5 deg north on the solstice the sun stays above -6 deg: sunset and sunrise only.
            Assert.Equal(name == "high-north-summer" ? 2 : 8, order.Count);
            foreach (var kind in new[] { NightEventKind.CivilDusk, NightEventKind.CivilDawn }) {
                if (info.EventMs(kind) is double ms) {
                    Assert.Equal(-6.0, SunAltitudeAt(info, ms), 6);
                }
            }

            Assert.Equal(sunset - NightInfoBuilder.AxisMarginMs, info.AxisStartMs);
            Assert.Equal(sunrise + NightInfoBuilder.AxisMarginMs, info.AxisEndMs);
        }

        [Theory]
        [InlineData(78.2, 15.6, "2025-06-21")]   // polar day
        [InlineData(78.2, 15.6, "2025-12-21")]   // polar night
        public void WithoutSunsetTheChartSpansTheWholeWindow(double lat, double lon, string date) {
            var info = NightInfoBuilder.Build(Bundle(lat, lon, DateOnly.Parse(date), 37.9, 89.3, 20.8), new[] { NightModel.LineFilter("Ha", 7) });
            Assert.Null(info.EventMs(NightEventKind.Sunset));
            Assert.Null(info.EventMs(NightEventKind.Sunrise));
            Assert.Equal(info.Bundle.Night.WindowStartMs, info.AxisStartMs);
            Assert.Equal(info.Bundle.Night.WindowEndMs, info.AxisEndMs);
        }

        [Theory]
        [InlineData("mid-north-fullmoon", LunarPhase.Full)]
        [InlineData("mid-north-newmoon", LunarPhase.New)]
        [InlineData("circumpolar", LunarPhase.LastQuarter)]      // 2025-11-11, a day before last quarter
        [InlineData("mid-north-autumn", LunarPhase.WaningCrescent)]
        public void TheMoonPhaseIsNamed(string name, LunarPhase phase) {
            var info = Info(name);
            Assert.Equal(phase, info.Moon.Phase);
            // Same tolerance as the golden test of the illumination.
            Assert.Equal(GoldenCase(name).GetProperty("night").GetProperty("illum").GetDouble(), info.Moon.IlluminationPercent, 1e-6);
        }

        /// <summary>From the night of 2025-09-18: new moon 2025-09-21 19:54 UTC, full moon 2025-10-07 03:48 UTC.</summary>
        [Fact]
        public void TheNextNewAndFullMoonAreFound() {
            var moon = Info("mid-north-autumn").Moon;
            var newMoon = NightModel.ToUtcDateTime(moon.NextNewMoonMs!.Value);
            var fullMoon = NightModel.ToUtcDateTime(moon.NextFullMoonMs!.Value);
            Assert.InRange(newMoon, new DateTime(2025, 9, 21, 19, 0, 0, DateTimeKind.Utc), new DateTime(2025, 9, 21, 21, 0, 0, DateTimeKind.Utc));
            Assert.InRange(fullMoon, new DateTime(2025, 10, 7, 3, 0, 0, DateTimeKind.Utc), new DateTime(2025, 10, 7, 5, 0, 0, DateTimeKind.Utc));
            Assert.False(moon.Waxing);
        }

        [Theory]
        [InlineData(5, 1.0, LunarPhase.New)]
        [InlineData(355, 1.0, LunarPhase.New)]
        [InlineData(30, 10, LunarPhase.WaxingCrescent)]
        [InlineData(90, 50, LunarPhase.FirstQuarter)]
        [InlineData(150, 90, LunarPhase.WaxingGibbous)]
        [InlineData(180, 99.5, LunarPhase.Full)]
        [InlineData(200, 95, LunarPhase.WaningGibbous)]
        [InlineData(270, 50, LunarPhase.LastQuarter)]
        [InlineData(330, 10, LunarPhase.WaningCrescent)]
        public void PhaseNamesFollowElongationAndIllumination(double phaseDeg, double illumination, LunarPhase phase) {
            Assert.Equal(phase, NightInfoBuilder.PhaseOf(phaseDeg, illumination));
        }

        /// <summary>
        /// The usable time is the target above the limit during astronomical night,
        /// and the separation figures describe exactly that time.
        /// </summary>
        [Theory]
        [InlineData("mid-north-autumn")]
        [InlineData("mid-north-fullmoon")]
        [InlineData("mid-north-winter")]
        public void UsableTimeAndMoonSeparationDescribeTheSameSpan(string name) {
            var info = Info(name);
            var settings = info.Settings;
            Assert.NotEmpty(info.Usable);
            foreach (var slot in info.AxisIndices().Select(info.Slot)) {
                var dark = info.Bundle.Night.AstronomicalNight.Any(i => i.Start <= slot.T && slot.T < i.End);
                var inUsable = info.Usable.Any(i => i.Start <= slot.T && slot.T < i.End);
                // Grid points next to an interpolated edge may fall either way.
                var nearEdge = info.Usable.Any(i => Math.Abs(i.Start - slot.T) < 120_000 || Math.Abs(i.End - slot.T) < 120_000);
                if (!nearEdge) {
                    Assert.Equal(dark && slot.TargetAltitude >= settings.MinAltitudeDeg, inUsable);
                }
            }

            var inside = info.Bundle.Sky.Samples.Where(s => info.Usable.Any(i => i.Start <= s.T && s.T < i.End)).ToList();
            Assert.Equal(inside.Min(s => s.MoonSeparationDeg), info.MinMoonSeparationDeg);
            Assert.Equal(inside.Max(s => s.MoonSeparationDeg), info.MaxMoonSeparationDeg);
            Assert.Equal(inside.Max(s => s.Magnitude), info.DarkestSkyMagnitude);
            Assert.Equal(info.UsableMinutes, NightModel.DurationMinutes(info.UsableMoonFree) + info.MoonUpWhileUsableMinutes, 6);
        }

        [Fact]
        public void ATargetThatNeverClimbsHasNoUsableTime() {
            var info = Info("low-target", NightModel.LineFilter("Ha", 7));
            Assert.Empty(info.TargetUp);
            Assert.Empty(info.Usable);
            Assert.Null(info.MinMoonSeparationDeg);
            Assert.Null(info.DarkestSkyMagnitude);
            Assert.Empty(info.Bands.Single().Segments);
        }

        /// <summary>The info and the conditions share one computation and agree on every window.</summary>
        [Fact]
        public void BandWindowsAreTheForecastWindows() {
            var c = GoldenCase("mid-north-autumn");
            var date = DateOnly.Parse(c.GetProperty("date").GetString()!);
            var moment = new DateTime(date.Year, date.Month, date.Day, 20, 0, 0, DateTimeKind.Utc);
            var forecaster = new WindowForecaster();
            double lat = c.GetProperty("lat").GetDouble(), lon = c.GetProperty("lon").GetDouble();
            double ra = c.GetProperty("ra").GetDouble(), dec = c.GetProperty("dec").GetDouble(), sqm = c.GetProperty("sqm").GetDouble();
            var bands = new[] { NightModel.BroadbandFilter(), NightModel.LineFilter("Ha", 7), NightModel.LineFilter("OIII", 7) };

            var info = NightInfoBuilder.Build(forecaster.Night(moment, Berlin, lat, lon, ra, dec, sqm), bands);
            foreach (var window in info.Bands) {
                var forecast = forecaster.Forecast(moment, Berlin, lat, lon, ra, dec, sqm, window.Band, NightModel.DefaultSettings);
                Assert.Equal(forecast.Segments, window.Segments);
                Assert.Equal(forecast.NeverOpensAtSite, window.NeverOpensAtSite);
                Assert.Equal(forecast.Threshold, window.Threshold);
                // Same grid rule for the shown values: a slot and a forecast sample are the same point.
                foreach (var offset in new[] { -60_000.0, 0.0, 60_000.0 }) {
                    if (forecast.Segments.Count > 0) {
                        var t = forecast.Segments[0].Start + offset;
                        Assert.Equal(forecast.SampleAt(t)!.T, info.SlotAt(t)!.T);
                    }
                }
            }
            Assert.Equal(1, forecaster.Computations);
        }

        [Fact]
        public void IntersectKeepsOnlyTheOverlap() {
            var a = new List<Interval> { new(0, 10), new(20, 30) };
            var b = new List<Interval> { new(5, 25) };
            Assert.Equal(new[] { new Interval(5, 10), new Interval(20, 25) }, NightInfoBuilder.Intersect(a, b));
            Assert.Empty(NightInfoBuilder.Intersect(a, new List<Interval> { new(10, 20) }));
        }

        [Fact]
        public void SlotsOutsideTheWindowAreNull() {
            var info = Info("mid-north-autumn");
            Assert.Null(info.SlotAt(info.Bundle.Night.WindowStartMs - 1));
            Assert.Null(info.SlotAt(info.Bundle.Night.WindowEndMs + 1));
            Assert.NotNull(info.SlotAt(info.Bundle.Night.WindowEndMs));
        }
    }
}
