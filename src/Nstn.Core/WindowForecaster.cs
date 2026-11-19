// What a sequencer condition needs at a given moment: which night it is, when the
// band's window is open that night, whether it is open right now, and when that
// changes. Kept out of the plugin so it can be tested without NINA.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nstn.Core {

    /// <summary>Everything the model knows about one night, one target and one site.</summary>
    public sealed record NightBundle(DateOnly NightDate, NightResult Night, TargetResult Target, SkyResult Sky);

    /// <summary>The window of one band over one night, for one target and site.</summary>
    public sealed record BandForecast(
        DateOnly NightDate,
        EvalFilter Band,
        IReadOnlyList<Interval> Segments,
        double Threshold,
        double MinimumSkyBrightness,
        double SqmZenith,
        SkyResult Sky) {

        public double OpenMinutes => NightModel.DurationMinutes(Segments);

        /// <summary>
        /// The site's own glow is already brighter than the band allows; moon and
        /// twilight can only add to it, so the window never opens, on any night.
        /// </summary>
        public bool NeverOpensAtSite => SqmZenith < MinimumSkyBrightness;

        public bool IsOpenAt(double epochMs) => Segments.Any(s => s.Start <= epochMs && epochMs < s.End);

        /// <summary>When the window next closes (if open) or opens (if closed); null if it stays as it is.</summary>
        public double? NextChangeAfter(double epochMs) {
            foreach (var segment in Segments) {
                if (segment.Start <= epochMs && epochMs < segment.End) {
                    return segment.End;
                }
                if (segment.Start > epochMs) {
                    return segment.Start;
                }
            }
            return null;
        }

        /// <summary>
        /// The last grid sample at or before a moment, or null outside the night's 24
        /// hours. Not the nearest one: the segments start at the first worthwhile
        /// sample, so a minute before an opening the nearest sample already passes
        /// while the window is still closed, and the shown value would contradict
        /// the shown state.
        /// </summary>
        public SkySample? SampleAt(double epochMs) {
            var samples = Sky.Samples;
            if (samples.Count == 0 || epochMs < samples[0].T || epochMs > samples[samples.Count - 1].T) {
                return null;
            }
            var step = NightModel.SampleStepMinutes * 60_000.0;
            var index = (int)Math.Floor((epochMs - samples[0].T) / step);
            return samples[Math.Clamp(index, 0, samples.Count - 1)];
        }

        public double? InBandMagnitudeAt(double epochMs) {
            var sample = SampleAt(epochMs);
            return sample == null ? null : NightModel.InBandMagnitude(sample, Band);
        }
    }

    /// <summary>
    /// Thread safe: the sequencer thread, the condition watchdog and the UI may ask
    /// at the same time. The expensive part - sun, moon and target over 721 grid
    /// points - is computed once per night, site, target and sky quality.
    /// </summary>
    public sealed class WindowForecaster {

        private const int MaxEntries = 64;

        private readonly object gate = new object();
        private readonly Dictionary<SkyKey, NightBundle> cache = new Dictionary<SkyKey, NightBundle>();

        public static WindowForecaster Shared { get; } = new WindowForecaster();

        /// <summary>Number of full sky computations so far - for tests and diagnostics.</summary>
        public int Computations { get; private set; }

        private readonly record struct SkyKey(DateOnly Night, double Lat, double Lon, string Tz, double Ra, double Dec, double Sqm);

        /// <summary>
        /// The night a moment belongs to: the local date of the evening before. The
        /// model's 24 hour window runs from local noon to local noon, so 03:00 on
        /// the 10th belongs to the night of the 9th - the same rule NINA uses.
        /// </summary>
        public static DateOnly NightDateFor(DateTime utcMoment, TimeZoneInfo timezone) {
            var utc = utcMoment.Kind == DateTimeKind.Utc ? utcMoment : utcMoment.ToUniversalTime();
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, timezone);
            var date = DateOnly.FromDateTime(local);
            return local.Hour < 12 ? date.AddDays(-1) : date;
        }

        public BandForecast Forecast(
            DateTime utcMoment,
            TimeZoneInfo timezone,
            double latitude,
            double longitude,
            double raDeg,
            double decDeg,
            double sqmZenith,
            EvalFilter band,
            WindowSettings settings) {

            var night = NightDateFor(utcMoment, timezone);
            var sky = Night(night, timezone, latitude, longitude, raDeg, decDeg, sqmZenith).Sky;
            return new BandForecast(
                NightDate: night,
                Band: band,
                Segments: NightModel.WorthwhileSegments(sky, band, settings),
                Threshold: settings.ThresholdFor(band),
                MinimumSkyBrightness: NightModel.MinimumSkyBrightness(band, settings),
                SqmZenith: sqmZenith,
                Sky: sky);
        }

        /// <summary>The window of a band with a filter's own limits (a filter plan row).</summary>
        public BandForecast Forecast(
            DateTime utcMoment,
            TimeZoneInfo timezone,
            double latitude,
            double longitude,
            double raDeg,
            double decDeg,
            double sqmZenith,
            EvalFilter band,
            WindowSettings settings,
            WindowLimits limits) {

            var forecast = Forecast(utcMoment, timezone, latitude, longitude, raDeg, decDeg, sqmZenith, band, limits.Apply(settings));
            return forecast with { Segments = limits.Clip(forecast.Segments, forecast.NightDate, timezone) };
        }

        /// <summary>Sun, moon, target and sky of the night a moment belongs to - shared with the forecasts.</summary>
        public NightBundle Night(DateTime utcMoment, TimeZoneInfo timezone, double latitude, double longitude, double raDeg, double decDeg, double sqmZenith) =>
            Night(NightDateFor(utcMoment, timezone), timezone, latitude, longitude, raDeg, decDeg, sqmZenith);

        private NightBundle Night(DateOnly night, TimeZoneInfo timezone, double latitude, double longitude, double raDeg, double decDeg, double sqm) {
            // Rounded to well below anything that moves a two minute grid: a target
            // nudged by a framing tool must not recompute the night every time.
            var key = new SkyKey(night, Math.Round(latitude, 4), Math.Round(longitude, 4), timezone.Id,
                Math.Round(raDeg, 4), Math.Round(decDeg, 4), Math.Round(sqm, 3));
            lock (gate) {
                if (cache.TryGetValue(key, out var cached)) {
                    return cached;
                }
            }

            var nightResult = NightModel.ComputeNight(latitude, longitude, night, timezone);
            var target = NightModel.ComputeTarget(nightResult, raDeg, decDeg);
            var bundle = new NightBundle(night, nightResult, target, NightModel.ComputeSky(nightResult, target, sqm));

            lock (gate) {
                if (cache.Count >= MaxEntries) {
                    cache.Clear();
                }
                cache[key] = bundle;
                Computations++;
            }
            return bundle;
        }
    }
}
