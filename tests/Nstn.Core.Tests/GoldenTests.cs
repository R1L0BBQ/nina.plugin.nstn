// The C# night model against the frozen output of the original implementation.
//
// Mirrors FITSORA's tests/test_night.py case for case and tolerance for
// tolerance. The model is calibrated; porting it is only safe if the port is
// shown to produce the same numbers. A difference here is a finding, not a
// fixture problem - see golden/README.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    public class GoldenTests {

        private static readonly JsonElement Golden = LoadGolden();

        // The original ran on central European wall clock time; the fixtures
        // inherit that, so the port is asked for the same window.
        private static readonly TimeZoneInfo FixtureTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        // Tolerances, identical to test_night.py. Both implementations work in
        // double precision, so anything beyond rounding noise is a real difference.
        private const double AngleTol = 1e-9;  // degrees
        private const double TimeTol = 1e-3;   // milliseconds, values interpolated between grid points
        // Anything the original returned as a JavaScript Date lost its fractional
        // millisecond on the way into the fixture.
        private const double DateTol = 1.0;
        private const double MagTol = 1e-8;    // mag/arcsec^2

        private static JsonElement LoadGolden() {
            var path = Path.Combine(AppContext.BaseDirectory, "golden", "night_model.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }

        public static IEnumerable<object[]> Cases() =>
            Golden.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! });

        private static JsonElement Case(string name) =>
            Golden.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

        private static int Stride => Golden.GetProperty("skyStride").GetInt32();

        private static NightResult Night(JsonElement c) =>
            NightModel.ComputeNight(
                c.GetProperty("lat").GetDouble(),
                c.GetProperty("lon").GetDouble(),
                DateOnly.Parse(c.GetProperty("date").GetString()!),
                FixtureTz);

        private static EvalFilter Band(JsonElement window) =>
            window.GetProperty("broadband").GetBoolean()
                ? NightModel.BroadbandFilter(window.GetProperty("bw").GetDouble())
                : NightModel.LineFilter(window.GetProperty("key").GetString()!, window.GetProperty("bw").GetDouble());

        private static void Close(double expected, double actual, double tolerance, string label) {
            Assert.True(Math.Abs(expected - actual) <= tolerance,
                $"{label}: expected {expected:R}, got {actual:R} (diff {Math.Abs(expected - actual):R}, tol {tolerance})");
        }

        private static void Relative(double expected, double actual, double relative, string label) {
            Close(expected, actual, Math.Abs(expected) * relative, label);
        }

        private static List<Interval> Intervals(JsonElement array) =>
            array.EnumerateArray().Select(i => new Interval(i[0].GetDouble(), i[1].GetDouble())).ToList();

        private static void SameIntervals(IReadOnlyList<Interval> expected, IReadOnlyList<Interval> actual, string label) {
            Assert.True(expected.Count == actual.Count, $"{label}: expected {expected.Count} intervals, got {actual.Count}");
            for (var i = 0; i < expected.Count; i++) {
                Close(expected[i].Start, actual[i].Start, TimeTol, $"{label}[{i}].start");
                Close(expected[i].End, actual[i].End, TimeTol, $"{label}[{i}].end");
            }
        }

        [Fact]
        public void ScalarConversionsMatch() {
            var scalars = Golden.GetProperty("scalars");
            Assert.Equal(NightModel.VWidthNm, scalars.GetProperty("V_WIDTH").GetDouble());
            foreach (var pair in scalars.GetProperty("mag2nl").EnumerateArray()) {
                Relative(pair[1].GetDouble(), NightModel.Mag2Nl(pair[0].GetDouble()), 1e-12, "mag2nl");
            }
            foreach (var pair in scalars.GetProperty("nl2mag").EnumerateArray()) {
                Relative(pair[1].GetDouble(), NightModel.Nl2Mag(pair[0].GetDouble()), 1e-12, "nl2mag");
            }
            foreach (var pair in scalars.GetProperty("wf").EnumerateArray()) {
                Relative(pair[1].GetDouble(), NightModel.ScatteredLightWeight(pair[0].GetDouble()), 1e-12, "wf");
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NightWindowAndSamplingMatch(string name) {
            var c = Case(name);
            var result = Night(c);
            var expected = c.GetProperty("night");
            Assert.Equal(expected.GetProperty("winStart").GetDouble(), result.WindowStartMs);
            Assert.Equal(expected.GetProperty("winEnd").GetDouble(), result.WindowEndMs);
            Assert.Equal(expected.GetProperty("sampleCount").GetInt32(), result.Times.Count);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SunAndMoonAltitudesMatch(string name) {
            var c = Case(name);
            var result = Night(c);
            var position = 0;
            foreach (var sample in c.GetProperty("sky").EnumerateArray()) {
                var index = position++ * Stride;
                Assert.Equal(sample.GetProperty("t").GetDouble(), result.Times[index]);
                Close(sample.GetProperty("sunAlt").GetDouble(), result.SunAltitude[index], AngleTol, $"sunAlt[{index}]");
                Close(sample.GetProperty("moonAlt").GetDouble(), result.MoonAltitude[index], AngleTol, $"moonAlt[{index}]");
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void TwilightCrossingsAndIntervalsMatch(string name) {
            var c = Case(name);
            var result = Night(c);
            var expected = c.GetProperty("night");

            var crossings = new (double? Actual, string Key)[] {
                (result.AstronomicalDuskMs, "astBeginEve"),
                (result.NightBeginMs, "nightBeginEve"),
                (result.NightEndMs, "nightEndMorn"),
                (result.AstronomicalDawnMs, "astEndMorn"),
                (result.MoonriseMs, "moonrise"),
                (result.MoonsetMs, "moonset"),
            };
            foreach (var (actual, key) in crossings) {
                var want = expected.GetProperty(key);
                if (want.ValueKind == JsonValueKind.Null) {
                    Assert.True(actual == null, $"{key}: expected none, got {actual}");
                } else {
                    Assert.True(actual != null, $"{key}: expected {want.GetDouble()}, got none");
                    Close(want.GetDouble(), actual!.Value, TimeTol, key);
                }
            }

            SameIntervals(Intervals(expected.GetProperty("astroNight")), result.AstronomicalNight, "astroNight");
            SameIntervals(Intervals(expected.GetProperty("moonUp")), result.MoonUp, "moonUp");
            SameIntervals(Intervals(expected.GetProperty("moonFree")), result.MoonFree, "moonFree");

            Close(expected.GetProperty("nightMin").GetDouble(), result.NightMinutes, 1e-6, "nightMin");
            Close(expected.GetProperty("moonFreeMin").GetDouble(), result.MoonFreeMinutes, 1e-6, "moonFreeMin");
            Assert.Equal(expected.GetProperty("hasNight").GetBoolean(), result.HasNight);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void AstronomicalMidnightAndIlluminationMatch(string name) {
            var c = Case(name);
            var result = Night(c);
            var expected = c.GetProperty("night");
            Close(expected.GetProperty("astroMidnight").GetDouble(), result.AstroMidnightMs, DateTol, "astroMidnight");
            Close(expected.GetProperty("illum").GetDouble(), result.MoonIllumination, 1e-6, "illum");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void TargetTrackMatches(string name) {
            var c = Case(name);
            var result = Night(c);
            var target = NightModel.ComputeTarget(result, c.GetProperty("ra").GetDouble(), c.GetProperty("dec").GetDouble());
            var expected = c.GetProperty("target");
            Close(expected.GetProperty("transit").GetDouble(), target.TransitMs, DateTol, "transit");
            Close(expected.GetProperty("transitAlt").GetDouble(), target.TransitAltitude, AngleTol, "transitAlt");
            Close(expected.GetProperty("maxAlt").GetDouble(), target.MaxAltitude, AngleTol, "maxAlt");
            Close(expected.GetProperty("minAlt").GetDouble(), target.MinAltitude, AngleTol, "minAlt");

            var position = 0;
            foreach (var sample in c.GetProperty("sky").EnumerateArray()) {
                var index = position++ * Stride;
                Close(sample.GetProperty("alt").GetDouble(), target.Altitude[index], AngleTol, $"alt[{index}]");
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SkyBackgroundMatches(string name) {
            var c = Case(name);
            var result = Night(c);
            var target = NightModel.ComputeTarget(result, c.GetProperty("ra").GetDouble(), c.GetProperty("dec").GetDouble());
            var sky = NightModel.ComputeSky(result, target, c.GetProperty("sqm").GetDouble());

            var position = 0;
            foreach (var expected in c.GetProperty("sky").EnumerateArray()) {
                var index = position++ * Stride;
                var sample = sky.Samples[index];
                Close(expected.GetProperty("mag").GetDouble(), sample.Magnitude, MagTol, $"mag[{index}]");
                Relative(expected.GetProperty("blp").GetDouble(), sample.LightPollutionNl, 1e-11, $"blp[{index}]");
                Relative(expected.GetProperty("bsc").GetDouble(), sample.ScatteredNl, 1e-8, $"bsc[{index}]");
            }
        }

        /// <summary>
        /// Pin down the last 1e-8 mag of disagreement, so it cannot hide anything:
        /// the original rounded astronomical midnight to a whole millisecond by
        /// passing it through a Date. Fed the same truncated reference, the port
        /// must reproduce the fixtures to 1e-11 mag.
        /// </summary>
        [Fact]
        public void SkyBackgroundResidualIsOnlyTheDateTruncation() {
            double worstFull = 0, worstTruncated = 0;
            foreach (var c in Golden.GetProperty("cases").EnumerateArray()) {
                var night = Night(c);
                var truncated = night with { AstroMidnightMs = Math.Floor(night.AstroMidnightMs) };
                foreach (var (variant, isFull) in new[] { (night, true), (truncated, false) }) {
                    var target = NightModel.ComputeTarget(variant, c.GetProperty("ra").GetDouble(), c.GetProperty("dec").GetDouble());
                    var sky = NightModel.ComputeSky(variant, target, c.GetProperty("sqm").GetDouble());
                    var worst = c.GetProperty("sky").EnumerateArray()
                        .Select((expected, position) => Math.Abs(sky.Samples[position * Stride].Magnitude - expected.GetProperty("mag").GetDouble()))
                        .Max();
                    if (isFull) {
                        worstFull = Math.Max(worstFull, worst);
                    } else {
                        worstTruncated = Math.Max(worstTruncated, worst);
                    }
                }
            }
            Assert.True(worstFull < 1e-8, $"full precision residual {worstFull:R}");
            Assert.True(worstTruncated < 1e-11, $"truncated residual {worstTruncated:R}");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void WorthwhileSegmentsMatch(string name) {
            var c = Case(name);
            var result = Night(c);
            var target = NightModel.ComputeTarget(result, c.GetProperty("ra").GetDouble(), c.GetProperty("dec").GetDouble());
            var sky = NightModel.ComputeSky(result, target, c.GetProperty("sqm").GetDouble());

            foreach (var window in c.GetProperty("windows").EnumerateArray()) {
                var band = Band(window);
                var label = $"{window.GetProperty("key").GetString()}/{window.GetProperty("bw").GetDouble()}";
                Relative(window.GetProperty("waveF").GetDouble(), band.WaveF, 1e-12, $"{label} waveF");
                SameIntervals(Intervals(window.GetProperty("segs")), NightModel.WorthwhileSegments(sky, band), label);
            }
        }

        [Fact]
        public void DuoFilterIsJudgedOnItsWeakLine() {
            var ha = NightModel.LineFilter("Ha", 6.0);
            var oiii = NightModel.LineFilter("OIII", 6.0);
            Close(2.95, oiii.WaveF / ha.WaveF, 0.01, "OIII/Ha Rayleigh ratio");
        }

        [Fact]
        public void MinimumSkyBrightnessMatchesTheWindow() {
            Close(20.17, NightModel.MinimumSkyBrightness(NightModel.LineFilter("OIII", 6.0)), 0.01, "6 nm floor");
            Close(20.6, NightModel.MinimumSkyBrightness(NightModel.BroadbandFilter()), 1e-12, "broadband floor");
        }

        [Fact]
        public void AirmassIsKastenYoung() {
            Assert.Equal(double.PositiveInfinity, NightModel.Airmass(0.0));
            Close(0.99971, NightModel.Airmass(90.0), 1e-5, "zenith");
            Close(1.995, NightModel.Airmass(30.0), 0.005, "30 deg");
        }

        [Fact]
        public void WindowStartFollowsTheLongitudeWithoutATimezone() {
            var date = new DateOnly(2025, 9, 18);
            var greenwich = NightModel.WindowStartMs(date, 0.0, null);
            var east = NightModel.WindowStartMs(date, 15.0, null);
            Close(3_600_000.0, greenwich - east, 1e-9, "one hour per 15 deg");
        }

        [Fact]
        public void SubtractAndMerge() {
            var remaining = NightModel.Subtract(new[] { new Interval(0, 100) }, new[] { new Interval(20, 30), new Interval(90, 120) });
            Assert.Equal(new[] { new Interval(0, 20), new Interval(30, 90) }, remaining);
            var merged = NightModel.MergeSegments(new[] { new Interval(0, 10), new Interval(15, 20), new Interval(40, 50) }, 5);
            Assert.Equal(new[] { new Interval(0, 20), new Interval(40, 50) }, merged);
        }
    }
}
