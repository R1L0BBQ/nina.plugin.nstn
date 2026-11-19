using System;
using System.Linq;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    public class WindowLimitsTests {

        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        private static readonly DateOnly Night = new DateOnly(2026, 11, 18);

        private static double Local(int day, int hour, int minute = 0) =>
            NightModel.ToEpochMs(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Unspecified), Berlin));

        [Fact]
        public void NoLimitsLeaveTheSettingsAsTheyAre() {
            Assert.True(WindowLimits.None.IsNone);
            Assert.Equal(NightModel.DefaultSettings, WindowLimits.None.Apply(NightModel.DefaultSettings));
        }

        [Fact]
        public void TheOffsetMovesBothThresholdsAndTheAltitudeReplacesTheSitesOne() {
            var applied = new WindowLimits(ThresholdOffset: 0.3, MinAltitudeDeg: 40).Apply(NightModel.DefaultSettings);
            Assert.Equal(NightModel.DefaultSettings.ThresholdBroadband + 0.3, applied.ThresholdBroadband, 9);
            Assert.Equal(NightModel.DefaultSettings.ThresholdNarrowband + 0.3, applied.ThresholdNarrowband, 9);
            Assert.Equal(40, applied.MinAltitudeDeg);
            Assert.Equal(NightModel.DefaultSettings.GapMinutes, applied.GapMinutes);
        }

        [Fact]
        public void ClockTimesBeforeNoonBelongToTheMorningAfter() {
            Assert.Equal(Local(18, 22), WindowLimits.NightMoment(Night, new TimeOnly(22, 0), Berlin));
            Assert.Equal(Local(19, 2), WindowLimits.NightMoment(Night, new TimeOnly(2, 0), Berlin));
            Assert.Equal(Local(18, 12), WindowLimits.NightMoment(Night, new TimeOnly(12, 0), Berlin));
        }

        [Fact]
        public void ASpanAcrossMidnightCutsTheWindow() {
            var segments = new[] { new Interval(Local(18, 20), Local(19, 4)) };
            var clipped = new WindowLimits(NotBefore: new TimeOnly(22, 0), NotAfter: new TimeOnly(2, 0)).Clip(segments, Night, Berlin);
            Assert.Equal(new[] { new Interval(Local(18, 22), Local(19, 2)) }, clipped);
        }

        [Fact]
        public void OnlyOneEndAndSegmentsOutsideTheSpan() {
            var segments = new[] { new Interval(Local(18, 19), Local(18, 21)), new Interval(Local(19, 1), Local(19, 5)) };
            Assert.Equal(new[] { new Interval(Local(19, 1), Local(19, 3)) },
                new WindowLimits(NotAfter: new TimeOnly(3, 0)).Clip(segments, Night, Berlin).Where(s => s.Start >= Local(19, 0)));
            Assert.Equal(new[] { new Interval(Local(19, 1), Local(19, 5)) },
                new WindowLimits(NotBefore: new TimeOnly(23, 30)).Clip(segments, Night, Berlin));
        }

        [Fact]
        public void ASpanThatEndsBeforeItStartsLeavesNothing() {
            var segments = new[] { new Interval(Local(18, 18), Local(19, 6)) };
            Assert.Empty(new WindowLimits(NotBefore: new TimeOnly(3, 0), NotAfter: new TimeOnly(23, 0)).Clip(segments, Night, Berlin));
        }

        [Theory]
        [InlineData("22:00", 22, 0)]
        [InlineData(" 7:30 ", 7, 30)]
        [InlineData("23.15", 23, 15)]
        [InlineData("5", 5, 0)]
        public void ClockTextIsRead(string text, int hour, int minute) {
            Assert.Equal(new TimeOnly(hour, minute), WindowLimits.ParseClock(text));
        }

        [Theory]
        [InlineData("")]
        [InlineData("25:00")]
        [InlineData("abends")]
        [InlineData(null)]
        public void UnreadableClockTextIsNoLimit(string? text) {
            Assert.Null(WindowLimits.ParseClock(text));
        }

        /// <summary>Through the forecaster: stricter, higher and shorter limits never widen a window.</summary>
        [Fact]
        public void LimitsOnlyNarrowTheForecast() {
            var forecaster = new WindowForecaster();
            var evening = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);
            var band = NightModel.LineFilter("Ha", 7);
            // M 33 from Kordel, SQM 21.3 (the moon sets at 02:06).
            BandForecast Forecast(WindowLimits limits) =>
                forecaster.Forecast(evening, Berlin, 49.8, 6.6, 23.46, 30.66, 21.3, band, NightModel.DefaultSettings, limits);

            var plain = Forecast(WindowLimits.None);
            Assert.True(plain.OpenMinutes > 60, $"control: the H-alpha window is open ({plain.OpenMinutes:0} min)");
            Assert.Equal(plain.Segments, forecaster.Forecast(evening, Berlin, 49.8, 6.6, 23.46, 30.66, 21.3, band, NightModel.DefaultSettings).Segments);

            var stricter = Forecast(new WindowLimits(ThresholdOffset: 0.5));
            var higher = Forecast(new WindowLimits(MinAltitudeDeg: 50));
            var looser = Forecast(new WindowLimits(ThresholdOffset: -0.5));
            var shorter = Forecast(new WindowLimits(NotBefore: new TimeOnly(23, 0), NotAfter: new TimeOnly(1, 0)));
            Assert.True(stricter.OpenMinutes < plain.OpenMinutes, $"stricter {stricter.OpenMinutes:0} < {plain.OpenMinutes:0}");
            Assert.True(higher.OpenMinutes < plain.OpenMinutes, $"higher {higher.OpenMinutes:0} < {plain.OpenMinutes:0}");
            Assert.True(looser.OpenMinutes >= plain.OpenMinutes, $"looser {looser.OpenMinutes:0} >= {plain.OpenMinutes:0}");
            Assert.True(shorter.OpenMinutes <= 120, $"23-01 h: at most two hours ({shorter.OpenMinutes:0})");
            Assert.All(shorter.Segments, s => Assert.True(s.Start >= Local(18, 23) && s.End <= Local(19, 1)));
        }
    }
}
