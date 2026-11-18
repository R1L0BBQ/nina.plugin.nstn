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

    /// <summary>One line of key data; <paramref name="Logged"/> lines also go to the log when the instruction runs.</summary>
    public sealed record ChartFact(string Label, string Text, bool Logged = false);

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
                Text.T("Chart_MoonUp"),
                Bars(night.MoonUp, X, 0.15 + 0.45 * illumination / 100.0),
                night.MoonUp.Count == 0 ? Text.T("Chart_MoonBelow") : Text.T("Chart_MoonLit", illumination),
                IsMoon: true));
            for (var b = 0; b < info.Bands.Count; b++) {
                var band = info.Bands[b];
                var label = b < bandLabels.Count ? bandLabels[b] : band.Band.Label;
                var summary = band.NeverOpensAtSite ? Text.T("Chart_NeverAtSite")
                    : band.Segments.Count == 0 ? Text.T("Chart_ClosedTonight")
                    : Text.Duration(band.OpenMinutes);
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
                LimitLabel: Text.T("Chart_Limit", settings.MinAltitudeDeg),
                SqmTop: SkyY(sqm),
                SqmLabel: string.Format(Text.Culture, "SQM {0:0.0#}", sqm),
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
                slot.TargetAltitude > 0
                    ? Text.T("Tip_Target", Degrees(slot.TargetAltitude), slot.Airmass)
                    : Text.T("Tip_TargetDown", Degrees(slot.TargetAltitude)),
                slot.MoonAltitude > 0
                    ? Text.T("Tip_MoonUp", Degrees(slot.MoonAltitude), slot.MoonSeparationDeg)
                    : Text.T("Tip_MoonDown", slot.MoonSeparationDeg),
                Text.T("Tip_Sky", slot.SkyMagnitude),
                Text.T("Tip_Sun", Degrees(slot.SunAltitude)),
                open.Count == 0 ? Text.T("Tip_NoneOpen") : Text.T("Tip_Open", string.Join(", ", open)),
            };
            return string.Join(Environment.NewLine, lines);
        }

        private static IReadOnlyList<ChartFact> Facts(NightInfo info, double nowMs, IReadOnlyList<string> bandLabels) {
            var facts = new List<ChartFact>();
            var night = info.Bundle.Night;
            string At(NightEventKind kind) => info.EventMs(kind) is double ms ? Clock(ms) : "–";

            var dark = night.HasNight
                ? Text.T("Fact_Dark", string.Join(", ", night.AstronomicalNight.Select(Span)), Text.Duration(night.NightMinutes))
                : Text.T("Fact_NoAstroNight");
            facts.Add(new ChartFact(Text.T("Fact_Night"),
                Text.T("Fact_NightText", Text.Date(info.Bundle.NightDate), At(NightEventKind.Sunset), dark, At(NightEventKind.Sunrise)), Logged: true));
            facts.Add(new ChartFact(Text.T("Fact_Twilight"), Text.T("Fact_TwilightText",
                At(NightEventKind.CivilDusk), At(NightEventKind.CivilDawn),
                At(NightEventKind.NauticalDusk), At(NightEventKind.NauticalDawn),
                At(NightEventKind.AstronomicalDusk), At(NightEventKind.AstronomicalDawn))));

            var moon = info.Moon;
            var moonParts = new List<string>();
            if (info.EventMs(NightEventKind.Moonrise) is double rise) {
                moonParts.Add(Text.T("Fact_MoonRises", Clock(rise)));
            }
            if (info.EventMs(NightEventKind.Moonset) is double set) {
                moonParts.Add(Text.T("Fact_MoonSets", Clock(set)));
            }
            if (moonParts.Count == 0) {
                moonParts.Add(night.MoonUp.Count == 0 ? Text.T("Fact_MoonBelowAllNight") : Text.T("Fact_MoonUpAllNight"));
            }
            if (night.HasNight) {
                moonParts.Add(Text.T("Fact_MoonFree", Text.Duration(night.MoonFreeMinutes)));
            }
            var nextMs = moon.Waxing ? moon.NextFullMoonMs : moon.NextNewMoonMs;
            if (nextMs is double next) {
                var date = Text.Date(DateOnly.FromDateTime(Local(next)));
                moonParts.Add(moon.Waxing ? Text.T("Fact_NextFull", date) : Text.T("Fact_NextNew", date));
            }
            facts.Add(new ChartFact(Text.T("Fact_Moon"),
                Text.T("Fact_MoonText", PhaseName(moon.Phase), moon.IlluminationPercent, string.Join(" · ", moonParts)), Logged: true));

            var target = info.Bundle.Target;
            var limit = info.Settings.MinAltitudeDeg;
            var targetParts = new List<string> { Text.T("Fact_Transit", Clock(target.TransitMs), Degrees(target.TransitAltitude)) };
            targetParts.Add(info.TargetUp.Count == 0
                ? Text.T("Fact_NeverAbove", limit)
                : Text.T("Fact_Above", limit, string.Join(", ", info.TargetUp.Select(Span))));
            if (info.Usable.Count > 0) {
                targetParts.Add(Text.T("Fact_Usable", Text.Duration(info.UsableMinutes), Text.Duration(NightModel.DurationMinutes(info.UsableMoonFree))));
            }
            facts.Add(new ChartFact(Text.T("Fact_Target"), string.Join(" · ", targetParts), Logged: true));

            if (info.MinMoonSeparationDeg is double minSeparation && info.MaxMoonSeparationDeg is double maxSeparation) {
                var range = Math.Round(maxSeparation) - Math.Round(minSeparation) < 1
                    ? $"{minSeparation:0}°"
                    : $"{minSeparation:0}°–{maxSeparation:0}°";
                var distance = Text.T("Fact_DistanceText", range, Text.Duration(info.MoonUpWhileUsableMinutes));
                if (info.DarkestSkyMagnitude is double magnitude) {
                    distance += " · " + Text.T("Fact_Darkest", magnitude);
                }
                facts.Add(new ChartFact(Text.T("Fact_MoonDistance"), distance));
            }

            var windows = info.Bands.Select((band, b) => {
                var label = b < bandLabels.Count ? bandLabels[b] : band.Band.Label;
                if (band.NeverOpensAtSite) {
                    return Text.T("Fact_WindowNeverAtSite", label);
                }
                return band.Segments.Count == 0
                    ? Text.T("Fact_WindowClosed", label)
                    : $"{label} {string.Join(", ", band.Segments.Select(Span))} ({Text.Duration(band.OpenMinutes)})";
            });
            facts.Add(new ChartFact(Text.T("Fact_Windows"), string.Join(" · ", windows), Logged: true));

            if (info.SlotAt(nowMs) is NightSlot now && nowMs >= info.AxisStartMs && nowMs <= info.AxisEndMs) {
                var moonNow = now.MoonAltitude > 0
                    ? Text.T("Fact_NowMoonUp", Degrees(now.MoonAltitude), now.MoonSeparationDeg)
                    : Text.T("Fact_NowMoonDown");
                facts.Add(new ChartFact(Text.T("Fact_Now"),
                    Text.T("Fact_NowText", Degrees(now.TargetAltitude), moonNow, now.SkyMagnitude, Degrees(now.SunAltitude))));
            }
            return facts;
        }

        public static string PhaseName(LunarPhase phase) => phase switch {
            LunarPhase.New => Text.T("Phase_New"),
            LunarPhase.WaxingCrescent => Text.T("Phase_WaxingCrescent"),
            LunarPhase.FirstQuarter => Text.T("Phase_FirstQuarter"),
            LunarPhase.WaxingGibbous => Text.T("Phase_WaxingGibbous"),
            LunarPhase.Full => Text.T("Phase_Full"),
            LunarPhase.WaningGibbous => Text.T("Phase_WaningGibbous"),
            LunarPhase.LastQuarter => Text.T("Phase_LastQuarter"),
            _ => Text.T("Phase_WaningCrescent"),
        };

        private static DateTime Local(double ms) => NightModel.ToUtcDateTime(ms).ToLocalTime();

        private static string Clock(double ms) => Text.Clock(Local(ms));

        private static string Span(Interval interval) => $"{Clock(interval.Start)}–{Clock(interval.End)}";

        private static string Degrees(double value) {
            var rounded = Math.Round(value);
            return rounded == 0 ? "0°" : $"{rounded:0}°".Replace("-", "−");
        }
    }
}
