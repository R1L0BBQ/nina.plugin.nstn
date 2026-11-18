using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NINA.Astrometry;
using NINA.Plugin.Nstn.FilterPlan;
using NINA.Plugin.Nstn.Instructions;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem.Connect;
using NINA.Sequencer.SequenceItem.Telescope;
using NINA.Sequencer.SequenceItem.Utility;
using NINA.Sequencer.Serialization;
using NINA.Sequencer.Trigger.Autofocus;
using NINA.Sequencer.Trigger.Guider;
using NINA.Sequencer.Trigger.MeridianFlip;

namespace NINA.Plugin.Nstn.Checks {

    /// <summary>
    /// Writes the target container for the acceptance run in N.I.N.A. with simulators
    /// (see tests/NinaTestInstance), with N.I.N.A.'s own serializer so the trigger
    /// runners come out exactly as N.I.N.A. expects them:
    ///
    /// a target at Dec +70° (always up in Kordel) whose meridian passage is
    /// FLIPTEST_MERIDIAN_MINUTES (default 10) from now, so the mount slews to it east of
    /// the meridian and the Meridian Flip trigger has to flip during the plan; 20 s wait
    /// (Alpaca discovery), connect all equipment, slew, Night Info (FLIPTEST_NIGHT_INFO=0
    /// leaves it out) and a filter plan with 2 × 5 s per filter; on the target Meridian
    /// Flip, Dither after every exposure and AF after filter change. FLIPTEST_ROUNDS sets
    /// "rounds per filter" (default 1).
    /// </summary>
    internal static class SequenceWriter {

        public static void WriteFlipTest(string path, IProfileService profile, SequenceJsonConverter converter, NstnFilterPlan template) {
            var minutes = double.Parse(Environment.GetEnvironmentVariable("FLIPTEST_MERIDIAN_MINUTES") ?? "10", CultureInfo.InvariantCulture);
            var rounds = int.Parse(Environment.GetEnvironmentVariable("FLIPTEST_ROUNDS") ?? "1", CultureInfo.InvariantCulture);
            // Sidereal time without N.I.N.A.'s database (UT1-UTC), good to about a second.
            var days = (DateTime.UtcNow - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
            var lst = AstroUtil.EuclidianModulus(18.697374558 + 24.06570982441908 * days + 6.6 / 15.0, 24);
            // JNow, as the mount takes it: no precession needed for the meridian passage.
            var jnow = new Coordinates(Angle.ByHours(AstroUtil.EuclidianModulus(lst + minutes / 60.0 * 1.0027379, 24)), Angle.ByDegree(70), Epoch.JNOW);

            var dso = Construct<DeepSkyObjectContainer>(profile);
            dso.Name = "Flip test – NSTN filter plan";
            dso.Target = new InputTarget(Angle.ByDegree(49.8), Angle.ByDegree(6.6), null) {
                TargetName = "Flip test Dec +70",
                InputCoordinates = new InputCoordinates(jnow),
            };
            dso.Add(Construct<MeridianFlipTrigger>(profile));
            var dither = Construct<DitherAfterExposures>(profile);
            dither.AfterExposures = 1;
            dso.Add(dither);
            dso.Add(Construct<AutofocusAfterFilterChange>(profile));

            // Started with --runsequence: give the Alpaca discovery time, then connect.
            var wait = Construct<WaitForTimeSpan>(profile);
            wait.Time = 20;
            dso.Add(wait);
            dso.Add(Construct<ConnectAllEquipment>(profile));
            dso.Add(Construct<SlewScopeToRaDec>(profile));
            // Without Night Info the plan sits high enough to be seen (N.I.N.A. only draws what is scrolled into view).
            if (Environment.GetEnvironmentVariable("FLIPTEST_NIGHT_INFO") != "0") {
                dso.Add(new NstnNightInfo(profile));
            }
            var plan = (NstnFilterPlan)template.Clone();
            plan.Rows.Clear();
            plan.Rows.Add(new FilterPlanRow { FilterName = "Askar D1", Count = 2, ExposureSeconds = 5 });
            plan.Rows.Add(new FilterPlanRow { FilterName = "Ha", Count = 2, ExposureSeconds = 5 });
            plan.Rows.Add(new FilterPlanRow { FilterName = "L", Count = 2, ExposureSeconds = 5 });
            plan.RoundsPerFilter = rounds;
            dso.Add(plan);

            File.WriteAllText(path, converter.Serialize(dso));
            Console.WriteLine($"written: {path} ({plan.ActualStructure})");
            Console.WriteLine(FormattableString.Invariant($"LST {lst:0.0000} h, meridian in {minutes} min at {DateTime.Now.AddMinutes(minutes):HH:mm:ss}; JNow RA {jnow.RA:0.0000} h, Dec {jnow.Dec:0.00}"));
        }

        /// <summary>What MEF does: the importing constructor, every interface a stub.</summary>
        private static T Construct<T>(IProfileService profile) {
            var ctor = typeof(T).GetConstructors().First(c => c.GetCustomAttribute<ImportingConstructorAttribute>() != null);
            var args = ctor.GetParameters()
                .Select(p => p.ParameterType == typeof(IProfileService) ? profile : p.ParameterType.IsInterface ? Stub.Create(p.ParameterType) : null)
                .ToArray();
            return (T)ctor.Invoke(args);
        }
    }
}
