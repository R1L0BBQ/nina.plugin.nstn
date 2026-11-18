using System;
using System.Diagnostics;

namespace NINA.Plugin.Nstn {

    /// <summary>
    /// The time NSTN decides with. In N.I.N.A. always the system clock. Only the check
    /// program (tests/NINA.Plugin.Nstn.Checks, via InternalsVisibleTo) lets it run from a
    /// fixed night, so that windows are open while the checks run in daytime. There is no
    /// file, setting or environment variable that could move it in a user's N.I.N.A.
    /// </summary>
    internal static class NstnClock {

        private static readonly object Gate = new object();
        private static readonly Stopwatch Since = new Stopwatch();
        private static DateTime? startUtc;

        public static DateTime UtcNow {
            get {
                lock (Gate) {
                    return startUtc is DateTime start ? start + Since.Elapsed : DateTime.UtcNow;
                }
            }
        }

        /// <summary>Checks only: from now on the clock runs on from this instant.</summary>
        internal static void RunFrom(DateTime utc) {
            lock (Gate) {
                startUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                Since.Restart();
            }
        }

        /// <summary>Checks only: back to the system clock.</summary>
        internal static void Reset() {
            lock (Gate) {
                startUtc = null;
                Since.Reset();
            }
        }
    }
}
