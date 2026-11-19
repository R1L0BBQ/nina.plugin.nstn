// A filter's own limits on top of the site's window settings: the rows of the
// filter plan. The model still decides whether the sky is good enough; these only
// make a window stricter or looser, cut it to a time span or raise its altitude.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nstn.Core {

    /// <param name="ThresholdOffset">Added to the site's threshold in mag/arcsec²: more is stricter, a shorter window.</param>
    /// <param name="MinAltitudeDeg">The target's minimum altitude for this filter; null keeps the site's.</param>
    /// <param name="NotBefore">Local clock time the window may open at the earliest; null for none.</param>
    /// <param name="NotAfter">Local clock time the window closes at the latest; null for none.</param>
    public sealed record WindowLimits(
        double ThresholdOffset = 0,
        double? MinAltitudeDeg = null,
        TimeOnly? NotBefore = null,
        TimeOnly? NotAfter = null) {

        public static readonly WindowLimits None = new WindowLimits();

        public bool IsNone => ThresholdOffset == 0 && MinAltitudeDeg == null && NotBefore == null && NotAfter == null;

        public WindowSettings Apply(WindowSettings settings) => settings with {
            ThresholdBroadband = settings.ThresholdBroadband + ThresholdOffset,
            ThresholdNarrowband = settings.ThresholdNarrowband + ThresholdOffset,
            MinAltitudeDeg = MinAltitudeDeg ?? settings.MinAltitudeDeg,
        };

        /// <summary>
        /// The segments within the time span of the night. Clock times from noon on
        /// belong to the evening of <paramref name="night"/>, earlier ones to the
        /// morning after - the same rule as <see cref="WindowForecaster.NightDateFor"/>.
        /// A span that ends before it starts leaves nothing.
        /// </summary>
        public IReadOnlyList<Interval> Clip(IReadOnlyList<Interval> segments, DateOnly night, TimeZoneInfo timezone) {
            if (NotBefore == null && NotAfter == null) {
                return segments;
            }
            var start = NotBefore is TimeOnly from ? NightMoment(night, from, timezone) : double.NegativeInfinity;
            var end = NotAfter is TimeOnly to ? NightMoment(night, to, timezone) : double.PositiveInfinity;
            return segments
                .Select(s => new Interval(Math.Max(s.Start, start), Math.Min(s.End, end)))
                .Where(s => s.End > s.Start)
                .ToList();
        }

        /// <summary>A clock time of the night as epoch milliseconds.</summary>
        public static double NightMoment(DateOnly night, TimeOnly time, TimeZoneInfo timezone) {
            var date = time.Hour >= 12 ? night : night.AddDays(1);
            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            // The hour skipped by daylight saving does not exist: take the one after.
            if (timezone.IsInvalidTime(local)) {
                local = local.AddHours(1);
            }
            return NightModel.ToEpochMs(TimeZoneInfo.ConvertTimeToUtc(local, timezone));
        }

        /// <summary>"HH:mm" as typed in a row; null for empty or unreadable text.</summary>
        public static TimeOnly? ParseClock(string? text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return null;
            }
            var trimmed = text.Trim();
            // "%H": a lone "H" would be read as a standard format and throw.
            foreach (var format in new[] { "H:mm", "HH:mm", "H.mm", "HH.mm", "%H", "HH" }) {
                if (TimeOnly.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) {
                    return time;
                }
            }
            return null;
        }
    }
}
