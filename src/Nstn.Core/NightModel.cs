// The night model: sky background, target track and the window per filter line.
//
// Ported from FITSORA's fitsora/domain/night.py, which itself is a literal port of
// the TypeScript exposure planner. The model is calibrated against several years
// of real nights, so this port is deliberately literal too - same sampling grid,
// same smoothing, same constants - and is pinned by the same golden fixtures
// generated from the original source (tests/golden/). A silent numeric drift here
// would move every verdict the plugin later acts on.
//
// Times are epoch milliseconds throughout, as in the original. That keeps the
// interval arithmetic trivial and the comparison against the fixtures exact;
// DateTime appears only at the boundaries.

using System;
using System.Collections.Generic;
using System.Globalization;
using CosineKitty;

namespace Nstn.Core {

    /// <summary>A half-open span of time in epoch milliseconds.</summary>
    public readonly record struct Interval(double Start, double End);

    /// <summary>Sun and moon over one 24 hour window, and what follows from them.</summary>
    public sealed record NightResult(
        double WindowStartMs,
        double WindowEndMs,
        double Latitude,
        double Longitude,
        IReadOnlyList<double> Times,
        IReadOnlyList<double> SunAltitude,
        IReadOnlyList<double> MoonAltitude,
        double? AstronomicalDuskMs,   // sun below -12 deg, evening
        double? NightBeginMs,         // sun below -18 deg, evening
        double? NightEndMs,
        double? AstronomicalDawnMs,
        IReadOnlyList<Interval> AstronomicalNight,
        IReadOnlyList<Interval> MoonUp,
        IReadOnlyList<Interval> MoonFree,
        double NightMinutes,
        double MoonFreeMinutes,
        double AstroMidnightMs,
        double MoonIllumination,      // per cent illuminated at astronomical midnight
        double? MoonriseMs,
        double? MoonsetMs) {

        public Observer Observer => new Observer(Latitude, Longitude, 0.0);

        public bool HasNight => AstronomicalNight.Count > 0;
    }

    /// <summary>The target's track over the same grid the night was sampled on.</summary>
    public sealed record TargetResult(
        IReadOnlyList<double> Altitude,
        double TransitMs,
        double TransitAltitude,
        double MaxAltitude,
        double MinAltitude,
        // Apparent coordinates at astronomical midnight, in radians. Kept here so the
        // sky model does not have to reach back into the library's global star slot.
        double RaOfDateRad,
        double DecOfDateRad);

    /// <summary>One grid point of the sky background in the direction of the target.</summary>
    public sealed record SkySample(
        double T,
        double Altitude,
        double Magnitude,          // total background, mag/arcsec^2
        double LightPollutionNl,   // the site's own glow, unaffected by wavelength
        double ScatteredNl,        // moon plus twilight - this is what the line weight acts on
        double TwilightNl,
        double MoonNl,
        double MoonSeparationDeg);

    public sealed record SkyResult(IReadOnlyList<SkySample> Samples, double SqmZenith);

    /// <summary>
    /// One band to be judged: a single emission line, or the whole V channel.
    /// A duo narrowband filter produces two of these, not one - the lines open and
    /// close at different times.
    /// </summary>
    public sealed record EvalFilter(string Key, string Label, bool Broadband, double WaveF, double BandwidthNm);

    /// <summary>
    /// Everything the verdict depends on. The thresholds are absolute in-band
    /// background brightness in mag/arcsec^2, higher meaning darker. They are
    /// calibrated defaults, not laws of nature: a bright site may want them lowered.
    /// </summary>
    public sealed record WindowSettings(
        double ThresholdBroadband = 20.6,
        double ThresholdNarrowband = 23.1,
        double MinAltitudeDeg = 20.0,
        double GapMinutes = 12.0) {

        public double ThresholdFor(EvalFilter band) => band.Broadband ? ThresholdBroadband : ThresholdNarrowband;
    }

    public static class NightModel {

        private const double Deg = Math.PI / 180.0;

        /// <summary>
        /// Extinction coefficient in V, in magnitudes per airmass. Deliberately
        /// independent of the line: what is line specific is the Rayleigh weight on
        /// the scattered light, not the extinction.
        /// </summary>
        public const double KExt = 0.20;

        /// <summary>Effective width of the V band in nanometres.</summary>
        public const double VWidthNm = 89.0;

        /// <summary>Representative wavelengths of the emission lines, in nanometres.</summary>
        public static readonly IReadOnlyDictionary<string, double> LineWavelengths = new Dictionary<string, double> {
            ["Ha"] = 656.3,
            ["OIII"] = 500.7,
            ["SII"] = 671.6,
            ["Hb"] = 486.1,
        };

        /// <summary>The night is sampled on a two minute grid, 721 points over 24 hours.</summary>
        public const int SampleStepMinutes = 2;

        public static readonly WindowSettings DefaultSettings = new WindowSettings();

        private const double J2000EpochMs = 946_728_000_000;  // 2000-01-01T12:00:00Z, the epoch of AstroTime.ut
        private const double MsPerDay = 86_400_000;
        private const double MsPerMinute = 60_000;

        // Astronomy.DefineStar writes into static state of the library. Only
        // ComputeTarget touches it, and it does so under this lock, so a second
        // sequencer thread cannot redefine the star another one is still reading.
        private static readonly object StarLock = new object();

        // -------------------------------------------------------------------
        // brightness conversions (Krisciunas & Schaefer 1991, eq. 27)
        // -------------------------------------------------------------------

        /// <summary>Surface brightness in mag/arcsec^2 to nanolambert.</summary>
        public static double Mag2Nl(double v) => 34.08 * Math.Exp(20.7233 - 0.92104 * v);

        /// <summary>Nanolambert to surface brightness in mag/arcsec^2.</summary>
        public static double Nl2Mag(double b) => (20.7233 - Math.Log(b / 34.08)) / 0.92104;

        /// <summary>
        /// Rayleigh weight of the scattered light in a band, relative to V. O III at
        /// 500.7 nm sees about three times what H-alpha at 656.3 nm sees from the same moon.
        /// </summary>
        public static double ScatteredLightWeight(double centerNm) => Math.Pow(550.0 / centerNm, 4);

        /// <summary>Airmass after Kasten &amp; Young (1989); infinite below the horizon.</summary>
        public static double Airmass(double altitudeDeg) {
            if (altitudeDeg <= 0) {
                return double.PositiveInfinity;
            }
            return 1.0 / (Math.Sin(altitudeDeg * Deg) + 0.50572 * Math.Pow(altitudeDeg + 6.07995, -1.6364));
        }

        // -------------------------------------------------------------------
        // time helpers
        // -------------------------------------------------------------------

        private static AstroTime Time(double epochMs) => new AstroTime((epochMs - J2000EpochMs) / MsPerDay);

        private static double EpochMs(AstroTime time) => time.ut * MsPerDay + J2000EpochMs;

        public static DateTime ToUtcDateTime(double epochMs) =>
            DateTime.UnixEpoch.AddTicks((long)Math.Round(epochMs * TimeSpan.TicksPerMillisecond));

        public static double ToEpochMs(DateTime moment) {
            var utc = moment.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(moment, DateTimeKind.Utc)
                : moment.ToUniversalTime();
            return (utc - DateTime.UnixEpoch).Ticks / (double)TimeSpan.TicksPerMillisecond;
        }

        /// <summary>
        /// Start of the 24 hour window: local noon before the night. With a timezone
        /// the wall clock decides. Without one the longitude decides - local mean
        /// solar noon - so any site still gets a window centred on its own midnight.
        /// </summary>
        public static double WindowStartMs(DateOnly nightDate, double longitude, TimeZoneInfo? timezone) {
            if (timezone != null) {
                var localNoon = new DateTime(nightDate.Year, nightDate.Month, nightDate.Day, 12, 0, 0, DateTimeKind.Unspecified);
                var offset = timezone.GetUtcOffset(localNoon);
                return new DateTimeOffset(localNoon, offset).ToUnixTimeMilliseconds();
            }
            var utcNoon = new DateTimeOffset(nightDate.Year, nightDate.Month, nightDate.Day, 12, 0, 0, TimeSpan.Zero);
            return utcNoon.ToUnixTimeMilliseconds() - longitude / 15.0 * 3_600_000.0;
        }

        // -------------------------------------------------------------------
        // interval helpers
        // -------------------------------------------------------------------

        /// <summary>Intervals where <paramref name="test"/> holds, with linearly interpolated edges.</summary>
        private static List<Interval> Regions(IReadOnlyList<double> times, IReadOnlyList<double> values, double threshold, Func<double, bool> test) {
            double Interpolate(int index) {
                double before = values[index - 1], after = values[index];
                if (before == after) {
                    return times[index - 1];
                }
                var span = times[index] - times[index - 1];
                return times[index - 1] + (threshold - before) / (after - before) * span;
            }

            var result = new List<Interval>();
            double? start = null;
            for (var index = 0; index < times.Count; index++) {
                if (test(values[index])) {
                    if (start == null) {
                        start = index > 0 ? Interpolate(index) : times[index];
                    }
                } else if (start != null) {
                    result.Add(new Interval(start.Value, Interpolate(index)));
                    start = null;
                }
            }
            if (start != null) {
                result.Add(new Interval(start.Value, times[times.Count - 1]));
            }
            return result;
        }

        /// <summary>First crossing of a threshold in the given direction, interpolated.</summary>
        private static double? FirstCross(IReadOnlyList<double> times, IReadOnlyList<double> values, double threshold, int direction) {
            for (var index = 1; index < times.Count; index++) {
                double before = values[index - 1], after = values[index];
                var crossed = direction < 0
                    ? before >= threshold && threshold > after
                    : before < threshold && threshold <= after;
                if (crossed) {
                    var span = times[index] - times[index - 1];
                    return times[index - 1] + (threshold - before) / (after - before) * span;
                }
            }
            return null;
        }

        /// <summary>Everything in <paramref name="baseSegments"/> that is not covered by <paramref name="cuts"/>.</summary>
        public static List<Interval> Subtract(IReadOnlyList<Interval> baseSegments, IReadOnlyList<Interval> cuts) {
            var segments = new List<Interval>(baseSegments);
            foreach (var cut in cuts) {
                var remaining = new List<Interval>();
                foreach (var segment in segments) {
                    if (cut.End <= segment.Start || cut.Start >= segment.End) {
                        remaining.Add(segment);
                        continue;
                    }
                    if (cut.Start > segment.Start) {
                        remaining.Add(new Interval(segment.Start, cut.Start));
                    }
                    if (cut.End < segment.End) {
                        remaining.Add(new Interval(cut.End, segment.End));
                    }
                }
                segments = remaining;
            }
            return segments.FindAll(segment => segment.End > segment.Start);
        }

        public static double DurationMinutes(IEnumerable<Interval> intervals) {
            var total = 0.0;
            foreach (var interval in intervals) {
                total += interval.End - interval.Start;
            }
            return total / MsPerMinute;
        }

        /// <summary>Close gaps shorter than <paramref name="gapMs"/>; they are sampling noise, not weather.</summary>
        public static List<Interval> MergeSegments(IReadOnlyList<Interval> segments, double gapMs) {
            if (segments.Count < 2) {
                return new List<Interval>(segments);
            }
            var result = new List<Interval> { segments[0] };
            for (var index = 1; index < segments.Count; index++) {
                var last = result[result.Count - 1];
                if (segments[index].Start - last.End <= gapMs) {
                    result[result.Count - 1] = new Interval(last.Start, segments[index].End);
                } else {
                    result.Add(segments[index]);
                }
            }
            return result;
        }

        // -------------------------------------------------------------------
        // the night
        // -------------------------------------------------------------------

        private static double AltitudeOf(Body body, AstroTime time, Observer observer) {
            var equatorial = Astronomy.Equator(body, time, observer, EquatorEpoch.OfDate, Aberration.Corrected);
            return Astronomy.Horizon(time, observer, equatorial.ra, equatorial.dec, Refraction.Normal).altitude;
        }

        /// <summary>Sample sun and moon over the 24 hours from local noon.</summary>
        public static NightResult ComputeNight(double latitude, double longitude, DateOnly nightDate, TimeZoneInfo? timezone = null) {
            var start = WindowStartMs(nightDate, longitude, timezone);
            var end = start + MsPerDay;
            var observer = new Observer(latitude, longitude, 0.0);

            var times = new List<double>();
            var sunAltitude = new List<double>();
            var moonAltitude = new List<double>();
            for (var minute = 0; minute <= 1440; minute += SampleStepMinutes) {
                var moment = start + minute * MsPerMinute;
                var time = Time(moment);
                times.Add(moment);
                sunAltitude.Add(AltitudeOf(Body.Sun, time, observer));
                moonAltitude.Add(AltitudeOf(Body.Moon, time, observer));
            }

            var astronomicalNight = Regions(times, sunAltitude, -18, v => v < -18);
            var moonUp = Regions(times, moonAltitude, 0, v => v > 0);
            var moonFree = Subtract(astronomicalNight, moonUp);

            double? astroMidnightMs = null;
            try {
                var hourAngle = Astronomy.SearchHourAngle(Body.Sun, observer, 12, Time(start));
                astroMidnightMs = EpochMs(hourAngle.time);
            } catch (Exception) {
                // polar night: no hour angle event in the window
                astroMidnightMs = null;
            }
            if (astroMidnightMs == null) {
                var lowest = 0;
                for (var i = 1; i < times.Count; i++) {
                    if (sunAltitude[i] < sunAltitude[lowest]) {
                        lowest = i;
                    }
                }
                astroMidnightMs = times[lowest];
            }

            var illumination = Astronomy.Illumination(Body.Moon, Time(astroMidnightMs.Value)).phase_fraction * 100.0;

            var epsilon = MsPerMinute;
            double? moonriseMs = null;
            double? moonsetMs = null;
            foreach (var interval in moonUp) {
                if (moonriseMs == null && interval.Start > start + epsilon) {
                    moonriseMs = interval.Start;
                }
                if (interval.End < end - epsilon) {
                    moonsetMs = interval.End;
                }
            }

            return new NightResult(
                WindowStartMs: start,
                WindowEndMs: end,
                Latitude: latitude,
                Longitude: longitude,
                Times: times,
                SunAltitude: sunAltitude,
                MoonAltitude: moonAltitude,
                AstronomicalDuskMs: FirstCross(times, sunAltitude, -12, -1),
                NightBeginMs: FirstCross(times, sunAltitude, -18, -1),
                NightEndMs: FirstCross(times, sunAltitude, -18, +1),
                AstronomicalDawnMs: FirstCross(times, sunAltitude, -12, +1),
                AstronomicalNight: astronomicalNight,
                MoonUp: moonUp,
                MoonFree: moonFree,
                NightMinutes: DurationMinutes(astronomicalNight),
                MoonFreeMinutes: DurationMinutes(moonFree),
                AstroMidnightMs: astroMidnightMs.Value,
                MoonIllumination: illumination,
                MoonriseMs: moonriseMs,
                MoonsetMs: moonsetMs);
        }

        // -------------------------------------------------------------------
        // the target
        // -------------------------------------------------------------------

        /// <summary>Altitude of a fixed target over the night, plus its transit.</summary>
        public static TargetResult ComputeTarget(NightResult night, double raDeg, double decDeg) {
            var observer = night.Observer;
            // DefineStar rejects 24h; a coordinate of exactly 360 deg is the same place as 0.
            var raHours = (((raDeg % 360.0) + 360.0) % 360.0) / 15.0;
            lock (StarLock) {
                // RA in hours, J2000; precession and aberration come from Equator(OfDate).
                Astronomy.DefineStar(Body.Star1, raHours, decDeg, 1000);
                var altitude = new List<double>(night.Times.Count);
                foreach (var moment in night.Times) {
                    altitude.Add(AltitudeOf(Body.Star1, Time(moment), observer));
                }

                var highest = 0;
                var lowest = double.PositiveInfinity;
                for (var i = 0; i < altitude.Count; i++) {
                    if (altitude[i] > altitude[highest]) {
                        highest = i;
                    }
                    lowest = Math.Min(lowest, altitude[i]);
                }
                var transitMs = night.Times[highest];
                var transitAltitude = altitude[highest];
                try {
                    var hourAngle = Astronomy.SearchHourAngle(Body.Star1, observer, 0, Time(night.WindowStartMs));
                    if (EpochMs(hourAngle.time) <= night.WindowEndMs) {
                        transitMs = EpochMs(hourAngle.time);
                        transitAltitude = hourAngle.hor.altitude;
                    }
                } catch (Exception) {
                    // circumpolar, or no transit inside the window
                }

                var apparent = Astronomy.Equator(Body.Star1, Time(night.AstroMidnightMs), observer, EquatorEpoch.OfDate, Aberration.Corrected);

                return new TargetResult(
                    Altitude: altitude,
                    TransitMs: transitMs,
                    TransitAltitude: transitAltitude,
                    MaxAltitude: altitude[highest],
                    MinAltitude: lowest,
                    RaOfDateRad: apparent.ra * 15.0 * Deg,
                    DecOfDateRad: apparent.dec * Deg);
            }
        }

        // -------------------------------------------------------------------
        // the sky background
        // -------------------------------------------------------------------

        /// <summary>
        /// Sky background towards the target: light pollution, twilight and moon.
        /// Light pollution and scattered light are kept apart on purpose. Only the
        /// scattered part is reweighted per emission line; the site's own glow is
        /// already measured in V and stays where it is.
        /// </summary>
        public static SkyResult ComputeSky(NightResult night, TargetResult target, double sqmZenith) {
            var observer = night.Observer;
            var reference = Time(night.AstroMidnightMs);
            var phaseAngle = Astronomy.Illumination(Body.Moon, reference).phase_angle;
            double targetRa = target.RaOfDateRad, targetDec = target.DecOfDateRad;

            var baseLightPollution = Mag2Nl(sqmZenith);
            var moonIntensity = Math.Pow(10, -0.4 * (3.84 + 0.026 * Math.Abs(phaseAngle) + 4e-9 * Math.Pow(phaseAngle, 4)));

            var samples = new List<SkySample>(night.Times.Count);
            for (var index = 0; index < night.Times.Count; index++) {
                var moment = night.Times[index];
                var time = Time(moment);
                var targetAltitude = target.Altitude[index];
                var sunAltitude = night.SunAltitude[index];
                var moonAltitude = night.MoonAltitude[index];

                var targetAirmass = Airmass(targetAltitude);
                var effectiveAirmass = double.IsFinite(targetAirmass) ? Math.Min(targetAirmass, 5.0) : 5.0;
                var horizonBoost = Math.Pow(effectiveAirmass, 0.7);

                var lightPollution = baseLightPollution * horizonBoost;
                // Twilight, empirical: rises with the sun and fades smoothly below
                // -18 deg instead of switching off at the boundary.
                var twilight = Mag2Nl(22 - 0.58 * (sunAltitude + 18)) * horizonBoost;

                var moonlight = 0.0;
                var moon = Astronomy.Equator(Body.Moon, time, observer, EquatorEpoch.OfDate, Aberration.Corrected);
                double moonRa = moon.ra * 15.0 * Deg, moonDec = moon.dec * Deg;
                var cosRho = Math.Sin(targetDec) * Math.Sin(moonDec)
                    + Math.Cos(targetDec) * Math.Cos(moonDec) * Math.Cos(targetRa - moonRa);
                var rho = Math.Acos(Math.Min(1.0, Math.Max(-1.0, cosRho)));
                var moonSeparationDeg = rho / Deg;
                if (moonAltitude > -4) {
                    // Krisciunas & Schaefer is not defined below the horizon.
                    var clampedMoonAlt = Math.Max(moonAltitude, 0.3);
                    var moonZenith = (90 - clampedMoonAlt) * Deg;
                    var targetZenith = (90 - Math.Max(targetAltitude, 0.1)) * Deg;
                    var scattering = Math.Pow(10, 5.36) * (1.06 + Math.Pow(Math.Cos(rho), 2)) + Math.Pow(10, 6.15 - (rho / Deg) / 40);
                    var moonX = Math.Pow(1 - 0.96 * Math.Pow(Math.Sin(moonZenith), 2), -0.5);
                    var targetX = Math.Pow(1 - 0.96 * Math.Pow(Math.Sin(targetZenith), 2), -0.5);
                    // Soft horizon crossing instead of a jump at 0 deg moon altitude.
                    var fade = 0.5 * (1 + Math.Tanh(moonAltitude / 1.5));
                    moonlight = scattering
                        * moonIntensity
                        * Math.Pow(10, -0.4 * KExt * moonX)
                        * (1 - Math.Pow(10, -0.4 * KExt * targetX))
                        * fade;
                    if (!double.IsFinite(moonlight) || moonlight < 0) {
                        moonlight = 0.0;
                    }
                }

                var total = lightPollution + twilight + moonlight;
                samples.Add(new SkySample(
                    T: moment,
                    Altitude: targetAltitude,
                    Magnitude: Nl2Mag(total),
                    LightPollutionNl: lightPollution,
                    ScatteredNl: twilight + moonlight,
                    TwilightNl: twilight,
                    MoonNl: moonlight,
                    MoonSeparationDeg: moonSeparationDeg));
            }
            return new SkyResult(samples, sqmZenith);
        }

        // -------------------------------------------------------------------
        // the window, per filter line
        // -------------------------------------------------------------------

        public static EvalFilter BroadbandFilter(double widthNm = VWidthNm, string label = "RGB") =>
            new EvalFilter("RGB", label, Broadband: true, WaveF: 1.0, BandwidthNm: widthNm);

        public static EvalFilter LineFilter(string line, double bandwidthNm, string? label = null) {
            var center = LineWavelengths[line];
            return new EvalFilter(
                line,
                label ?? $"{line} {bandwidthNm.ToString("G", CultureInfo.InvariantCulture)} nm",
                Broadband: false,
                WaveF: ScatteredLightWeight(center),
                BandwidthNm: bandwidthNm);
        }

        /// <summary>
        /// Background brightness inside a band, in mag/arcsec^2. A narrow passband
        /// collects only its share of the background (bandwidth / V width, darkening
        /// it); a blue line collects more scattered light (WaveF, brightening it).
        /// </summary>
        public static double InBandMagnitude(SkySample sample, EvalFilter band) {
            var bandFraction = band.Broadband ? 1.0 : band.BandwidthNm / VWidthNm;
            return Nl2Mag((sample.LightPollutionNl + sample.ScatteredNl * band.WaveF) * bandFraction);
        }

        /// <summary>
        /// Darkest the site itself must be for this band to open at all. Moon and
        /// twilight only add brightness, so a band whose threshold the site's own
        /// glow cannot clear never opens - worth saying instead of showing an
        /// empty window that looks like a broken calculation.
        /// </summary>
        public static double MinimumSkyBrightness(EvalFilter band, WindowSettings? settings = null) {
            var threshold = (settings ?? DefaultSettings).ThresholdFor(band);
            if (band.Broadband) {
                return threshold;
            }
            return threshold + 2.5 * Math.Log10(band.BandwidthNm / VWidthNm);
        }

        /// <summary>Target high enough, and the band's sky darker than its threshold.</summary>
        public static bool IsWorthwhile(SkySample sample, EvalFilter band, WindowSettings? settings = null) {
            settings ??= DefaultSettings;
            if (sample.Altitude < settings.MinAltitudeDeg) {
                return false;
            }
            return InBandMagnitude(sample, band) >= settings.ThresholdFor(band);
        }

        /// <summary>The intervals of the night in which this band is worth exposing.</summary>
        public static List<Interval> WorthwhileSegments(SkyResult sky, EvalFilter band, WindowSettings? settings = null) {
            settings ??= DefaultSettings;
            if (sky.Samples.Count == 0) {
                return new List<Interval>();
            }
            var last = sky.Samples[sky.Samples.Count - 1].T;
            var raw = new List<Interval>();
            double? start = null;
            foreach (var sample in sky.Samples) {
                if (IsWorthwhile(sample, band, settings)) {
                    start ??= sample.T;
                } else if (start != null) {
                    raw.Add(new Interval(start.Value, sample.T));
                    start = null;
                }
            }
            if (start != null) {
                raw.Add(new Interval(start.Value, last));
            }
            return MergeSegments(raw, settings.GapMinutes * MsPerMinute);
        }
    }
}
