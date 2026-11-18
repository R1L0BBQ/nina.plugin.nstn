// When a filter plan is done for the night: no band of it is open now and none opens
// later tonight. Replaces a fixed end time ("Loop Until Time"), which only fits one
// night.

using System.Collections.Generic;
using System.Linq;

namespace Nstn.Core {

    public static class PlanEnd {

        /// <summary>
        /// True while at least one window is open at <paramref name="epochMs"/> or opens
        /// after it in the same night. Unusable bands (null) do not count; with none
        /// usable the plan has nothing left to do.
        /// </summary>
        public static bool AnyWindowLeft(IEnumerable<BandForecast?> forecasts, double epochMs) =>
            forecasts.Where(f => f != null).Any(f => f!.IsOpenAt(epochMs) || f!.NextChangeAfter(epochMs) != null);

        /// <summary>The end of the last window of the night, null if none opens at all.</summary>
        public static double? LastClose(IEnumerable<BandForecast?> forecasts) =>
            forecasts.Where(f => f != null && f.Segments.Count > 0)
                .Select(f => (double?)f!.Segments[f.Segments.Count - 1].End)
                .DefaultIfEmpty(null)
                .Max();
    }
}
