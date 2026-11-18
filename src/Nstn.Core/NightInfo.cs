// The night at a glance for one target: when sun and moon do what, how far the
// moon stands from the target, and the sky towards it - everything the Night Info
// instruction shows. Built from the same NightBundle the windows come from, so the
// chart and the conditions cannot disagree.

using System;
using System.Collections.Generic;
using System.Linq;
using CosineKitty;

namespace Nstn.Core {

    public enum NightEventKind {
        Sunset,
        CivilDusk,          // sun below -6 deg
        NauticalDusk,       // below -12 deg
        AstronomicalDusk,   // below -18 deg: the night begins
        AstronomicalDawn,
        NauticalDawn,
        CivilDawn,
        Sunrise,
        Moonrise,
        Moonset,
        Transit,
    }

    public sealed record NightEvent(NightEventKind Kind, double Ms);

    public enum LunarPhase { New, WaxingCrescent, FirstQuarter, WaxingGibbous, Full, WaningGibbous, LastQuarter, WaningCrescent }

    /// <summary>The moon at astronomical midnight, the moment the model's illumination refers to.</summary>
    public sealed record MoonInfo(
        double PhaseDeg,               // 0 new, 90 first quarter, 180 full, 270 last quarter
        double IlluminationPercent,
        LunarPhase Phase,
        double? NextNewMoonMs,
        double? NextFullMoonMs) {

        public bool Waxing => PhaseDeg < 180;
    }

    /// <summary>One band's window over the night, as the NSTN conditions see it.</summary>
    public sealed record BandWindow(EvalFilter Band, IReadOnlyList<Interval> Segments, double Threshold, bool NeverOpensAtSite) {
        public double OpenMinutes => NightModel.DurationMinutes(Segments);
    }

    /// <summary>The state at one grid point - what a tooltip or the "now" line shows.</summary>
    public sealed record NightSlot(
        double T,
        double SunAltitude,
        double MoonAltitude,
        double TargetAltitude,
        double MoonSeparationDeg,
        double SkyMagnitude) {

        public double Airmass => NightModel.Airmass(TargetAltitude);
    }

    public sealed record NightInfo(
        NightBundle Bundle,
        WindowSettings Settings,
        double AxisStartMs,
        double AxisEndMs,
        IReadOnlyList<NightEvent> Events,
        MoonInfo Moon,
        IReadOnlyList<Interval> TargetUp,         // at or above the minimum altitude
        IReadOnlyList<Interval> Usable,           // target up during astronomical night
        IReadOnlyList<Interval> UsableMoonFree,   // ... and the moon below the horizon
        double? MinMoonSeparationDeg,             // over the usable time
        double? MaxMoonSeparationDeg,
        double MoonUpWhileUsableMinutes,
        double? DarkestSkyMagnitude,              // towards the target, over the usable time
        IReadOnlyList<BandWindow> Bands) {

        public double UsableMinutes => NightModel.DurationMinutes(Usable);

        public double? EventMs(NightEventKind kind) => Events.FirstOrDefault(e => e.Kind == kind)?.Ms;

        /// <summary>The last grid point at or before a moment - the same rule as <see cref="BandForecast.SampleAt"/>.</summary>
        public NightSlot? SlotAt(double epochMs) {
            var times = Bundle.Night.Times;
            if (times.Count == 0 || epochMs < times[0] || epochMs > times[times.Count - 1]) {
                return null;
            }
            var index = (int)Math.Floor((epochMs - times[0]) / (NightModel.SampleStepMinutes * 60_000.0));
            return Slot(Math.Clamp(index, 0, times.Count - 1));
        }

        public NightSlot Slot(int index) {
            var sample = Bundle.Sky.Samples[index];
            return new NightSlot(
                T: sample.T,
                SunAltitude: Bundle.Night.SunAltitude[index],
                MoonAltitude: Bundle.Night.MoonAltitude[index],
                TargetAltitude: Bundle.Target.Altitude[index],
                MoonSeparationDeg: sample.MoonSeparationDeg,
                SkyMagnitude: sample.Magnitude);
        }

        /// <summary>Grid indices between the axis ends, both included.</summary>
        public IEnumerable<int> AxisIndices() {
            var times = Bundle.Night.Times;
            for (var i = 0; i < times.Count; i++) {
                if (times[i] >= AxisStartMs && times[i] <= AxisEndMs) {
                    yield return i;
                }
            }
        }
    }

    public static class NightInfoBuilder {

        /// <summary>How far the chart reaches past sunset and sunrise.</summary>
        public const double AxisMarginMs = 30 * 60_000;

        public static NightInfo Build(NightBundle bundle, IEnumerable<EvalFilter> bands, WindowSettings? settings = null) {
            settings ??= NightModel.DefaultSettings;
            var night = bundle.Night;
            var times = night.Times;
            var sun = night.SunAltitude;

            var events = new List<NightEvent>();
            void Add(NightEventKind kind, double? ms) {
                if (ms != null && ms >= night.WindowStartMs && ms <= night.WindowEndMs) {
                    events.Add(new NightEvent(kind, ms.Value));
                }
            }

            var observer = night.Observer;
            var sunset = RiseSet(observer, Direction.Set, night.WindowStartMs, night.WindowEndMs);
            var sunrise = RiseSet(observer, Direction.Rise, sunset ?? night.WindowStartMs, night.WindowEndMs);
            Add(NightEventKind.Sunset, sunset);
            Add(NightEventKind.CivilDusk, NightModel.FirstCrossing(times, sun, -6, -1));
            Add(NightEventKind.NauticalDusk, night.AstronomicalDuskMs);   // the model calls -12 deg "astronomical dusk"
            Add(NightEventKind.AstronomicalDusk, night.NightBeginMs);
            Add(NightEventKind.AstronomicalDawn, night.NightEndMs);
            Add(NightEventKind.NauticalDawn, night.AstronomicalDawnMs);
            Add(NightEventKind.CivilDawn, NightModel.FirstCrossing(times, sun, -6, +1));
            Add(NightEventKind.Sunrise, sunrise);
            Add(NightEventKind.Moonrise, night.MoonriseMs);
            Add(NightEventKind.Moonset, night.MoonsetMs);
            Add(NightEventKind.Transit, bundle.Target.TransitMs);
            events.Sort((a, b) => a.Ms.CompareTo(b.Ms));

            // Sunset to sunrise plus a margin; without either (polar day or night) the whole window.
            var axisStart = sunset == null ? night.WindowStartMs : Math.Max(night.WindowStartMs, sunset.Value - AxisMarginMs);
            var axisEnd = sunrise == null ? night.WindowEndMs : Math.Min(night.WindowEndMs, sunrise.Value + AxisMarginMs);

            var targetUp = NightModel.AtOrAbove(times, bundle.Target.Altitude, settings.MinAltitudeDeg);
            var usable = Intersect(targetUp, night.AstronomicalNight);
            var usableMoonFree = NightModel.Subtract(usable, night.MoonUp);

            double? minSeparation = null, maxSeparation = null, darkest = null;
            foreach (var sample in bundle.Sky.Samples) {
                if (!Inside(usable, sample.T)) {
                    continue;
                }
                minSeparation = Math.Min(minSeparation ?? double.MaxValue, sample.MoonSeparationDeg);
                maxSeparation = Math.Max(maxSeparation ?? double.MinValue, sample.MoonSeparationDeg);
                darkest = Math.Max(darkest ?? double.MinValue, sample.Magnitude);
            }

            var windows = bands
                .Select(band => new BandWindow(
                    band,
                    NightModel.WorthwhileSegments(bundle.Sky, band, settings),
                    settings.ThresholdFor(band),
                    bundle.Sky.SqmZenith < NightModel.MinimumSkyBrightness(band, settings)))
                .ToList();

            return new NightInfo(
                Bundle: bundle,
                Settings: settings,
                AxisStartMs: axisStart,
                AxisEndMs: axisEnd,
                Events: events,
                Moon: Moon(night),
                TargetUp: targetUp,
                Usable: usable,
                UsableMoonFree: usableMoonFree,
                MinMoonSeparationDeg: minSeparation,
                MaxMoonSeparationDeg: maxSeparation,
                MoonUpWhileUsableMinutes: NightModel.DurationMinutes(usable) - NightModel.DurationMinutes(usableMoonFree),
                DarkestSkyMagnitude: darkest,
                Bands: windows);
        }

        /// <summary>The phase from the elongation, with a little room around new, full and the quarters.</summary>
        public static LunarPhase PhaseOf(double phaseDeg, double illuminationPercent) {
            var waxing = phaseDeg < 180;
            if (illuminationPercent < 2) {
                return LunarPhase.New;
            }
            if (illuminationPercent > 98) {
                return LunarPhase.Full;
            }
            if (Math.Abs(illuminationPercent - 50) < 6) {
                return waxing ? LunarPhase.FirstQuarter : LunarPhase.LastQuarter;
            }
            if (illuminationPercent < 50) {
                return waxing ? LunarPhase.WaxingCrescent : LunarPhase.WaningCrescent;
            }
            return waxing ? LunarPhase.WaxingGibbous : LunarPhase.WaningGibbous;
        }

        private static MoonInfo Moon(NightResult night) {
            var midnight = NightModel.Time(night.AstroMidnightMs);
            var phase = Astronomy.MoonPhase(midnight);
            double? Next(double longitude) {
                var found = Astronomy.SearchMoonPhase(longitude, midnight, 40);
                return found == null ? null : NightModel.EpochMs(found);
            }
            return new MoonInfo(phase, night.MoonIllumination, PhaseOf(phase, night.MoonIllumination), Next(0), Next(180));
        }

        private static double? RiseSet(Observer observer, Direction direction, double fromMs, double untilMs) {
            if (untilMs <= fromMs) {
                return null;
            }
            var found = Astronomy.SearchRiseSet(Body.Sun, observer, direction, NightModel.Time(fromMs), (untilMs - fromMs) / 86_400_000.0);
            return found == null ? null : NightModel.EpochMs(found);
        }

        private static bool Inside(IReadOnlyList<Interval> intervals, double ms) =>
            intervals.Any(i => i.Start <= ms && ms < i.End);

        public static List<Interval> Intersect(IReadOnlyList<Interval> a, IReadOnlyList<Interval> b) {
            var result = new List<Interval>();
            foreach (var x in a) {
                foreach (var y in b) {
                    var start = Math.Max(x.Start, y.Start);
                    var end = Math.Min(x.End, y.End);
                    if (end > start) {
                        result.Add(new Interval(start, end));
                    }
                }
            }
            result.Sort((p, q) => p.Start.CompareTo(q.Start));
            return result;
        }
    }
}
