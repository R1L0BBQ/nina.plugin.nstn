using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nstn.Core;

namespace NINA.Plugin.Nstn.Instructions {

    public sealed record ChartRect(double Left, double Width, double Opacity);

    public sealed record ChartTick(double Left, string Label);

    public sealed record ChartSlot(double Left, double Width, string Tip);

    public sealed record ChartBandRow(string Label, IReadOnlyList<ChartRect> Bars, string Summary, bool IsMoon = false);

    public sealed record ChartFact(string Label, string Text);

    /// <summary>
    /// The night of one target, drawn to scale for the Night Info instruction:
    /// altitude of target and moon over the twilight, the moon's distance, the sky
    /// towards the target and one row per band. Built off the UI thread; the point
    /// collections are frozen so WPF may read them from its own.
    /// </summary>
    public sealed record NightChart(
        bool HasChart,
        IReadOnlyList<ChartFact> Facts,
        PointCollection TargetAltitude,
        PointCollection MoonAltitude,
        PointCollection MoonSeparation,
        PointCollection SkyBrightness,
        IReadOnlyList<ChartRect> Twilight,
        double LimitTop,
        string LimitLabel,
        double SqmTop,
        string SqmLabel,
        IReadOnlyList<ChartBandRow> Rows,
        IReadOnlyList<ChartTick> Ticks,
        IReadOnlyList<ChartSlot> Slots,
        bool NowVisible,
        double NowLeft,
        double TransitLeft,
        bool TransitVisible) {

        public const double Width = 480;
        public const double AltitudeHeight = 100;
        public const double SeparationHeight = 34;
        public const double SkyHeight = 44;
        public const double SkyBright = 16;   // mag/arcsec^2 at the bottom
        public const double SkyDark = 22;     // ... and at the top

        public static NightChart Empty { get; } = new NightChart(false, Array.Empty<ChartFact>(),
            Frozen(new PointCollection()), Frozen(new PointCollection()), Frozen(new PointCollection()), Frozen(new PointCollection()),
            Array.Empty<ChartRect>(), 0, string.Empty, 0, string.Empty, Array.Empty<ChartBandRow>(), Array.Empty<ChartTick>(),
            Array.Empty<ChartSlot>(), false, 0, 0, false);

        /// <summary>Total height of the three curve panels plus their gaps, for the hover overlay.</summary>
        public static double CurvesHeight => AltitudeHeight + SeparationHeight + SkyHeight + 2 * 4;

        internal static PointCollection Frozen(PointCollection points) {
            points.Freeze();
            return points;
        }
    }

    internal static class NightChartBuilder {

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // Tooltips cover the chart in slots of this many grid points (2 min each).
        private const int SlotSamples = 5;

        public static NightChart Build(NightInfo info, double nowMs, IReadOnlyList<string> bandLabels) {
            var night = info.Bundle.Night;
            var target = info.Bundle.Target;
            var sky = info.Bundle.Sky.Samples;
            var start = info.AxisStartMs;
            var span = Math.Max(info.AxisEndMs - start, 60_000.0);
            double X(double t) => Math.Clamp((t - start) / span, 0, 1) * NightChart.Width;
            static double Clamp01(double v) => Math.Clamp(v, 0, 1);
            double AltitudeY(double altitude) => (1 - Clamp01(altitude / 90.0)) * NightChart.AltitudeHeight;
            double SeparationY(double degrees) => (1 - Clamp01(degrees / 180.0)) * NightChart.SeparationHeight;
            double SkyY(double magnitude) =>
                (1 - Clamp01((magnitude - NightChart.SkyBright) / (NightChart.SkyDark - NightChart.SkyBright))) * NightChart.SkyHeight;

            var indices = info.AxisIndices().ToList();
            var targetLine = new PointCollection(indices.Count);
            var moonLine = new PointCollection(indices.Count);
            var separationLine = new PointCollection(indices.Count);
            var skyLine = new PointCollection(indices.Count);
            foreach (var i in indices) {
                var x = X(night.Times[i]);
                targetLine.Add(new Point(x, AltitudeY(target.Altitude[i])));
                moonLine.Add(new Point(x, AltitudeY(night.MoonAltitude[i])));
                separationLine.Add(new Point(x, SeparationY(sky[i].MoonSeparationDeg)));
                skyLine.Add(new Point(x, SkyY(sky[i].Magnitude)));
            }

            var rows = new List<ChartBandRow>();
            var illumination = info.Moon.IlluminationPercent;
            rows.Add(new ChartBandRow(
                "Moon up",
                Bars(night.MoonUp, X, 0.15 + 0.45 * illumination / 100.0),
                night.MoonUp.Count == 0 ? "below the horizon" : $"{illumination:0} % lit",
                IsMoon: true));
            for (var b = 0; b < info.Bands.Count; b++) {
                var band = info.Bands[b];
                var label = b < bandLabels.Count ? bandLabels[b] : band.Band.Label;
                var summary = band.NeverOpensAtSite ? "never at this site"
                    : band.Segments.Count == 0 ? "closed tonight"
                    : Duration(band.OpenMinutes);
                rows.Add(new ChartBandRow(label, Bars(band.Segments, X, 0.85), summary));
            }

            var settings = info.Settings;
            var sqm = info.Bundle.Sky.SqmZenith;
            var transit = target.TransitMs;
            return new NightChart(
                HasChart: true,
                Facts: Facts(info, nowMs, bandLabels),
                TargetAltitude: NightChart.Frozen(targetLine),
                MoonAltitude: NightChart.Frozen(moonLine),
                MoonSeparation: NightChart.Frozen(separationLine),
                SkyBrightness: NightChart.Frozen(skyLine),
                Twilight: Twilight(info, indices, X),
                LimitTop: AltitudeY(settings.MinAltitudeDeg),
                LimitLabel: $"{settings.MinAltitudeDeg:0}°",
                SqmTop: SkyY(sqm),
                SqmLabel: $"SQM {sqm.ToString("0.0#", Invariant)}",
                Rows: rows,
                Ticks: Ticks(start, info.AxisEndMs, X),
                Slots: Slots(info, indices, X, bandLabels),
                NowVisible: nowMs >= start && nowMs <= info.AxisEndMs,
                NowLeft: X(nowMs),
                TransitLeft: X(transit),
                TransitVisible: transit >= start && transit <= info.AxisEndMs);
        }

        private static IReadOnlyList<ChartRect> Bars(IEnumerable<Interval> intervals, Func<double, double> x, double opacity) =>
            intervals
                .Select(i => new ChartRect(x(i.Start), Math.Max(1.0, x(i.End) - x(i.Start)), opacity))
                .Where(r => r.Left < NightChart.Width)
                .ToList();

        /// <summary>Brighter backgrounds for a brighter sky: day, civil, nautical, astronomical twilight; night stays clear.</summary>
        private static IReadOnlyList<ChartRect> Twilight(NightInfo info, List<int> indices, Func<double, double> x) {
            static double Shade(double sunAltitude) =>
                sunAltitude > -0.833 ? 0.30 : sunAltitude > -6 ? 0.21 : sunAltitude > -12 ? 0.13 : sunAltitude > -18 ? 0.06 : 0.0;

            var rects = new List<ChartRect>();
            var times = info.Bundle.Night.Times;
            var sun = info.Bundle.Night.SunAltitude;
            for (var k = 0; k < indices.Count;) {
                var shade = Shade(sun[indices[k]]);
                var end = k;
                while (end + 1 < indices.Count && Shade(sun[indices[end + 1]]) == shade) {
                    end++;
                }
                if (shade > 0) {
                    var left = x(times[indices[k]]);
                    var right = end + 1 < indices.Count ? x(times[indices[end + 1]]) : NightChart.Width;
                    rects.Add(new ChartRect(left, Math.Max(1.0, right - left), shade));
                }
                k = end + 1;
            }
            return rects;
        }

        private static IReadOnlyList<ChartTick> Ticks(double startMs, double endMs, Func<double, double> x) {
            var local = Local(startMs);
            var first = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Local).AddHours(1);
            var hours = (endMs - startMs) / 3_600_000.0;
            var every = hours > 12 ? 2 : 1;
            var ticks = new List<ChartTick>();
            for (var tick = first; ; tick = tick.AddHours(1)) {
                var ms = NightModel.ToEpochMs(tick.ToUniversalTime());
                if (ms > endMs) {
                    break;
                }
                if (tick.Hour % every == 0) {
                    ticks.Add(new ChartTick(x(ms), tick.ToString("HH", Invariant)));
                }
            }
            return ticks;
        }

        private static IReadOnlyList<ChartSlot> Slots(NightInfo info, List<int> indices, Func<double, double> x, IReadOnlyList<string> bandLabels) {
            var slots = new List<ChartSlot>();
            for (var k = 0; k < indices.Count; k += SlotSamples) {
                var slot = info.Slot(indices[k]);
                var nextT = k + SlotSamples < indices.Count ? info.Bundle.Night.Times[indices[k + SlotSamples]] : info.AxisEndMs;
                var open = info.Bands
                    .Select((band, b) => (band, label: b < bandLabels.Count ? bandLabels[b] : band.Band.Label))
                    .Where(p => p.band.Segments.Any(s => s.Start <= slot.T && slot.T < s.End))
                    .Select(p => p.label)
                    .ToList();
                var left = x(slot.T);
                slots.Add(new ChartSlot(left, Math.Max(1.0, x(nextT) - left), Tip(slot, open)));
            }
            return slots;
        }

        private static string Tip(NightSlot slot, IReadOnlyList<string> open) {
            var lines = new List<string> {
                Clock(slot.T),
                $"Target {Degrees(slot.TargetAltitude)}" + (slot.TargetAltitude > 0 ? $" · airmass {slot.Airmass.ToString("0.00", Invariant)}" : " (below the horizon)"),
                slot.MoonAltitude > 0
                    ? $"Moon {Degrees(slot.MoonAltitude)} · {slot.MoonSeparationDeg:0}° from the target"
                    : $"Moon below the horizon · {slot.MoonSeparationDeg:0}° from the target",
                $"Sky towards the target {slot.SkyMagnitude.ToString("0.0", Invariant)} mag/arcsec²",
                $"Sun {Degrees(slot.SunAltitude)}",
                open.Count == 0 ? "No window open" : $"Open: {string.Join(", ", open)}",
            };
            return string.Join(Environment.NewLine, lines);
        }

        private static IReadOnlyList<ChartFact> Facts(NightInfo info, double nowMs, IReadOnlyList<string> bandLabels) {
            var facts = new List<ChartFact>();
            var night = info.Bundle.Night;
            string At(NightEventKind kind) => info.EventMs(kind) is double ms ? Clock(ms) : "–";

            var dark = night.HasNight
                ? $"dark {string.Join(", ", night.AstronomicalNight.Select(Span))} ({Duration(night.NightMinutes)})"
                : "no astronomical night";
            facts.Add(new ChartFact("Night", $"{DateLabel(info.Bundle.NightDate)} · sunset {At(NightEventKind.Sunset)} · {dark} · sunrise {At(NightEventKind.Sunrise)}"));
            facts.Add(new ChartFact("Twilight",
                $"civil {At(NightEventKind.CivilDusk)} / {At(NightEventKind.CivilDawn)} · nautical {At(NightEventKind.NauticalDusk)} / {At(NightEventKind.NauticalDawn)} · astronomical {At(NightEventKind.AstronomicalDusk)} / {At(NightEventKind.AstronomicalDawn)}"));

            var moon = info.Moon;
            var moonTimes = new List<string>();
            if (info.EventMs(NightEventKind.Moonrise) is double rise) {
                moonTimes.Add($"rises {Clock(rise)}");
            }
            if (info.EventMs(NightEventKind.Moonset) is double set) {
                moonTimes.Add($"sets {Clock(set)}");
            }
            if (moonTimes.Count == 0) {
                moonTimes.Add(night.MoonUp.Count == 0 ? "below the horizon all night" : "up all night");
            }
            var nextPhase = moon.Waxing ? ("full moon", moon.NextFullMoonMs) : ("new moon", moon.NextNewMoonMs);
            var next = nextPhase.Item2 is double nextMs ? $" · next {nextPhase.Item1} {DateLabel(DateOnly.FromDateTime(NightModel.ToUtcDateTime(nextMs).ToLocalTime()))}" : string.Empty;
            var moonFree = night.HasNight ? $" · moon-free dark {Duration(night.MoonFreeMinutes)}" : string.Empty;
            facts.Add(new ChartFact("Moon", $"{PhaseName(moon.Phase)}, {moon.IlluminationPercent:0} % lit · {string.Join(", ", moonTimes)}{moonFree}{next}"));

            var target = info.Bundle.Target;
            var targetText = $"transit {Clock(target.TransitMs)} at {Degrees(target.TransitAltitude)}";
            targetText += info.TargetUp.Count == 0
                ? $" · never above {info.Settings.MinAltitudeDeg:0}°"
                : $" · above {info.Settings.MinAltitudeDeg:0}° {string.Join(", ", info.TargetUp.Select(Span))}";
            if (info.Usable.Count > 0) {
                targetText += $" · usable {Duration(info.UsableMinutes)}, of which moon-free {Duration(NightModel.DurationMinutes(info.UsableMoonFree))}";
            }
            facts.Add(new ChartFact("Target", targetText));

            if (info.MinMoonSeparationDeg is double minSeparation && info.MaxMoonSeparationDeg is double maxSeparation) {
                var range = Math.Round(maxSeparation) - Math.Round(minSeparation) < 1
                    ? $"{minSeparation:0}°"
                    : $"{minSeparation:0}°–{maxSeparation:0}°";
                var darkest = info.DarkestSkyMagnitude is double magnitude ? $" · darkest sky towards it {magnitude.ToString("0.0", Invariant)} mag/arcsec²" : string.Empty;
                facts.Add(new ChartFact("Moon distance", $"{range} while usable · moon up {Duration(info.MoonUpWhileUsableMinutes)} of that time{darkest}"));
            }

            var windows = info.Bands.Select((band, b) => {
                var label = b < bandLabels.Count ? bandLabels[b] : band.Band.Label;
                if (band.NeverOpensAtSite) {
                    return $"{label} never at this site";
                }
                return band.Segments.Count == 0
                    ? $"{label} closed"
                    : $"{label} {string.Join(", ", band.Segments.Select(Span))} ({Duration(band.OpenMinutes)})";
            });
            facts.Add(new ChartFact("Windows", string.Join(" · ", windows)));

            if (info.SlotAt(nowMs) is NightSlot now && nowMs >= info.AxisStartMs && nowMs <= info.AxisEndMs) {
                var moonNow = now.MoonAltitude > 0 ? $"moon {Degrees(now.MoonAltitude)}, {now.MoonSeparationDeg:0}° away" : "moon below the horizon";
                facts.Add(new ChartFact("Now", $"target {Degrees(now.TargetAltitude)} · {moonNow} · sky {now.SkyMagnitude.ToString("0.0", Invariant)} mag/arcsec² · sun {Degrees(now.SunAltitude)}"));
            }
            return facts;
        }

        public static string PhaseName(LunarPhase phase) => phase switch {
            LunarPhase.New => "New moon",
            LunarPhase.WaxingCrescent => "Waxing crescent",
            LunarPhase.FirstQuarter => "First quarter",
            LunarPhase.WaxingGibbous => "Waxing gibbous",
            LunarPhase.Full => "Full moon",
            LunarPhase.WaningGibbous => "Waning gibbous",
            LunarPhase.LastQuarter => "Last quarter",
            _ => "Waning crescent",
        };

        private static DateTime Local(double ms) => NightModel.ToUtcDateTime(ms).ToLocalTime();

        private static string Clock(double ms) => Local(ms).ToString("HH:mm", Invariant);

        private static string Span(Interval interval) => $"{Clock(interval.Start)}–{Clock(interval.End)}";

        private static string DateLabel(DateOnly date) => date.ToString("d MMM", Invariant);

        private static string Degrees(double value) {
            var rounded = Math.Round(value);
            return rounded == 0 ? "0°" : $"{rounded:0}°".Replace("-", "−");
        }

        public static string Duration(double minutes) {
            var total = (int)Math.Round(minutes);
            return total >= 60 ? $"{total / 60} h {total % 60} min" : $"{total} min";
        }
    }
}
