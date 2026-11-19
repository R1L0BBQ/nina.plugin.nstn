using System;
using System.Linq;
using Nstn.Core;
using Xunit;

namespace Nstn.Core.Tests {

    /// <summary>The end of a filter plan without a clock time.</summary>
    public class PlanEndTests {

        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        private static BandForecast Window(params (double Start, double End)[] seconds) =>
            new BandForecast(new DateOnly(2026, 11, 17), NightModel.LineFilter("Ha", 7),
                seconds.Select(s => new Interval(s.Start * 1000, s.End * 1000)).ToArray(),
                23.1, 20.34, 21.0, new SkyResult(Array.Empty<SkySample>(), 21.0));

        [Fact]
        public void LeftWhileOpenOrOpeningLater() {
            var a = Window((100, 200));
            var b = Window((300, 400));
            Assert.True(PlanEnd.AnyWindowLeft(new[] { a, b }, 0));        // both still to come
            Assert.True(PlanEnd.AnyWindowLeft(new[] { a, b }, 150_000));  // a open
            Assert.True(PlanEnd.AnyWindowLeft(new[] { a, b }, 250_000));  // gap: b opens later
            Assert.True(PlanEnd.AnyWindowLeft(new[] { a, b }, 399_999));
            Assert.False(PlanEnd.AnyWindowLeft(new[] { a, b }, 400_000)); // half-open: b closed at its end
            Assert.Equal(400_000, PlanEnd.LastClose(new[] { a, b }));
        }

        [Fact]
        public void NothingLeftWithoutUsableOrOpeningBands() {
            Assert.False(PlanEnd.AnyWindowLeft(new BandForecast?[] { null, Window() }, 0));
            Assert.False(PlanEnd.AnyWindowLeft(Array.Empty<BandForecast?>(), 0));
            Assert.Null(PlanEnd.LastClose(new[] { Window() }));
        }

        [Fact]
        public void RealNightEndsAfterTheLastWindowNotAtAFixedTime() {
            // M 31 from Kordel (49.8 N, 6.6 E), SQM 20.9, night of 17/18 Nov 2026:
            // the plan ends with the last of its windows, whatever time that is.
            var forecaster = new WindowForecaster();
            var evening = new DateTime(2026, 11, 17, 17, 0, 0, DateTimeKind.Utc);
            var bands = new[] { NightModel.LineFilter("Ha", 7), NightModel.LineFilter("OIII", 7) };
            var forecasts = bands.Select(b => forecaster.Forecast(evening, Berlin, 49.8, 6.6, 10.68, 41.27, 20.9, b, NightModel.DefaultSettings)).ToArray();
            var last = PlanEnd.LastClose(forecasts);
            Assert.NotNull(last);
            Assert.True(PlanEnd.AnyWindowLeft(forecasts, NightModel.ToEpochMs(evening)));
            Assert.True(PlanEnd.AnyWindowLeft(forecasts, last.Value - 60_000));
            Assert.False(PlanEnd.AnyWindowLeft(forecasts, last.Value));
            // Later the same night (dawn, before sunrise) nothing comes back.
            var dawn = new DateTime(2026, 11, 18, 6, 0, 0, DateTimeKind.Utc);
            var lateForecasts = bands.Select(b => forecaster.Forecast(dawn, Berlin, 49.8, 6.6, 10.68, 41.27, 20.9, b, NightModel.DefaultSettings)).ToArray();
            Assert.Equal(forecasts[0].NightDate, lateForecasts[0].NightDate);
            Assert.False(PlanEnd.AnyWindowLeft(lateForecasts, NightModel.ToEpochMs(dawn)));
            // After sunrise the forecasts are the coming night's: a run keeps its own night
            // (NstnNightLeftCondition, run E of the N.I.N.A. checks), the display moves on.
            var morning = new DateTime(2026, 11, 18, 10, 30, 0, DateTimeKind.Utc);
            Assert.Equal(forecasts[0].NightDate.AddDays(1), forecaster.Forecast(morning, Berlin, 49.8, 6.6, 10.68, 41.27, 20.9, bands[0], NightModel.DefaultSettings).NightDate);
        }
    }
}
