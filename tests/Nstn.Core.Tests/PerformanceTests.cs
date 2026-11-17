using System;
using System.Diagnostics;
using System.Linq;
using Nstn.Core;
using Xunit;
using Xunit.Abstractions;

namespace Nstn.Core.Tests {

    /// <summary>
    /// How much a night costs, printed for comparison across machines. The limit is
    /// deliberately generous - a guard against an accidental order of magnitude,
    /// not a benchmark that fails on a busy build host.
    /// </summary>
    public class PerformanceTests {

        private readonly ITestOutputHelper output;

        public PerformanceTests(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void OneNightForOneTargetIsCheap() {
            var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
            var date = new DateOnly(2026, 11, 9);
            var bands = new[] {
                NightModel.BroadbandFilter(), NightModel.LineFilter("Ha", 7), NightModel.LineFilter("OIII", 7),
                NightModel.LineFilter("SII", 7), NightModel.LineFilter("Ha", 12), NightModel.LineFilter("OIII", 12),
            };

            // Warm up the JIT, then take the median of several runs.
            Run();
            var timings = Enumerable.Range(0, 7).Select(_ => Run()).OrderBy(t => t.Total).ToList();
            var median = timings[timings.Count / 2];
            output.WriteLine($"night {median.Night:F1} ms, target {median.Target:F1} ms, sky {median.Sky:F1} ms, windows (6 bands) {median.Windows:F2} ms, total {median.Total:F1} ms");

            var sample = new WindowForecaster();
            sample.Forecast(new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc), berlin, 49.8, 6.6, 314.75, 44.33, 20.9, bands[1], NightModel.DefaultSettings);
            var watch = Stopwatch.StartNew();
            const int checks = 1000;
            for (var i = 0; i < checks; i++) {
                var forecast = sample.Forecast(new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc), berlin, 49.8, 6.6, 314.75, 44.33, 20.9, bands[2], NightModel.DefaultSettings);
                forecast.IsOpenAt(forecast.Sky.Samples[300].T);
            }
            var perCheck = watch.Elapsed.TotalMilliseconds / checks;
            output.WriteLine($"cached check (one band, open state) {perCheck * 1000:F0} µs");

            Assert.True(median.Total < 2000, $"one night took {median.Total:F0} ms");
            Assert.Equal(1, sample.Computations);

            (double Night, double Target, double Sky, double Windows, double Total) Run() {
                var watchRun = Stopwatch.StartNew();
                var night = NightModel.ComputeNight(49.8, 6.6, date, berlin);
                var t1 = watchRun.Elapsed.TotalMilliseconds;
                var target = NightModel.ComputeTarget(night, 314.75, 44.33);
                var t2 = watchRun.Elapsed.TotalMilliseconds;
                var sky = NightModel.ComputeSky(night, target, 20.9);
                var t3 = watchRun.Elapsed.TotalMilliseconds;
                var open = bands.Sum(b => NightModel.DurationMinutes(NightModel.WorthwhileSegments(sky, b)));
                var t4 = watchRun.Elapsed.TotalMilliseconds;
                Assert.True(open > 0);  // reach: a new moon night, something must open
                return (t1, t2 - t1, t3 - t2, t4 - t3, t4);
            }
        }
    }
}
