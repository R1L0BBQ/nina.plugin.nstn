using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NINA.Astrometry;
using NINA.Core.Locale;
using NINA.Core.Model.Equipment;
using NINA.Plugin.Nstn.FilterPlan;
using NINA.Plugin.Nstn.Instructions;
using NINA.Profile.Interfaces;
using NINA.Sequencer;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Autofocus;
using NINA.Sequencer.SequenceItem.Camera;
using NINA.Sequencer.SequenceItem.Connect;
using NINA.Sequencer.SequenceItem.Guider;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.SequenceItem.Telescope;
using NINA.Sequencer.SequenceItem.Utility;
using NINA.Sequencer.Serialization;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Trigger.Autofocus;
using NINA.Sequencer.Trigger.Guider;
using NINA.Sequencer.Trigger.MeridianFlip;
using NINA.Sequencer.Utility.DateTimeProvider;
using Nstn.Core;

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
    /// "rounds per filter" (default 1); FLIPTEST_VIEW_ONLY=1 writes the target with the plan only.
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
            // FLIPTEST_VIEW_ONLY=1: only the plan, to look at its view (nothing above it to scroll past).
            var viewOnly = Environment.GetEnvironmentVariable("FLIPTEST_VIEW_ONLY") == "1";
            if (!viewOnly) {
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
            }
            // Without Night Info the plan sits high enough to be seen (N.I.N.A. only draws what is scrolled into view).
            if (!viewOnly && Environment.GetEnvironmentVariable("FLIPTEST_NIGHT_INFO") != "0") {
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

        /// <summary>
        /// Writes the sample sequence to share (samples/NSTN-LRGB-Ha.json), a complete
        /// sequence for "Load sequence". A complete sequence cannot be a template:
        /// N.I.N.A.'s TemplateController skips root containers.
        ///
        /// Start: cool to -10 °C, unpark. Target M 33 with Meridian Flip, Dither after 3
        /// exposures and AF after time (60 min, on the profile's autofocus filter); inside
        /// Night Info, wait for nautical dusk and 20° altitude (centering needs stars and the
        /// target above the horizon), center, autofocus, start guiding, filter plan L/R/G/B
        /// 5 × 120 s and Ha 5 × 300 s, 2 sets in a row (fewer filter changes). End: stop
        /// guiding, warm, park. Names in the language written (en-US or de-DE), as N.I.N.A.'s
        /// plugin loader gives them; N.I.N.A. keeps saved names.
        /// </summary>
        public static ISequenceRootContainer WriteShareable(string path, IProfileService profile, SequenceJsonConverter converter, NstnFilterPlan template, string language = "en-US") {
            Loc.Instance.ReloadLocale(language);
            var textCulture = Text.Culture;
            Text.Culture = CultureInfo.GetCultureInfo(language);
            try {
                return WriteShareable(path, profile, converter, template, Notes[language]);
            } finally {
                Text.Culture = textCulture;
            }
        }

        /// <summary>The annotations of the sample, per language: Marc loads the German one in a German N.I.N.A.</summary>
        private static readonly Dictionary<string, (string Start, string Target, string Focus, string End)> Notes = new Dictionary<string, (string, string, string, string)> {
            ["en-US"] = (
                "Prepares the equipment. Set the cooling temperature for your camera; connect the equipment before you start the sequence.",
                "Requires the NSTN plugin 0.9.0 or later and the SQM value of your site in Options > Plugins > NSTN. " +
                "Set your target here (name search, Framing or Sky Atlas). The filter names in the NSTN filter plan must match " +
                "the filter wheel list of your profile: pick yours in each row. NSTN Night Info shows the night and checks the setup.",
                "Autofocus once here, then every hour (trigger 'AF After Time', 60 min). For it to focus on L, two settings of the profile: " +
                "tick 'Autofocus filter' for L in the filter list (Options > Equipment > Filterwheel) and switch on 'Use filterwheel offsets' " +
                "(Options > Equipment > Focuser). N.I.N.A. then changes to L for the autofocus and back afterwards; the NSTN filter plan " +
                "goes on with its own filter. Enter the focus offsets of the other filters relative to L.",
                "Runs once the filter plan has ended: no NSTN window opens again this night."),
            ["de-DE"] = (
                "Bereitet die Ausrüstung vor. Die Kühltemperatur für die eigene Kamera einstellen und die Ausrüstung verbinden, bevor die Sequenz startet.",
                "Braucht das NSTN-Plugin 0.9.0 oder neuer und den SQM-Wert des Standorts unter Optionen > Plugins > NSTN. " +
                "Das Ziel hier setzen (Namenssuche, Rahmung oder Sternenatlas). Die Filternamen im NSTN-Filterplan müssen zur " +
                "Filterradliste des Profils passen: in jeder Zeile den eigenen Filter wählen. Die NSTN-Nachtinfo zeigt die Nacht und prüft den Aufbau.",
                "Autofokus einmal hier, danach jede Stunde (Auslöser „AF nach Zeit“, 60 min). Damit auf L fokussiert wird, braucht das Profil zwei " +
                "Einstellungen: in der Filterliste (Optionen > Ausrüstung > Filterrad) bei L „Autofokus Filter“ anhaken und unter Optionen > " +
                "Ausrüstung > Okularauszug „Filterrad Offsets nutzen“ einschalten. N.I.N.A. wechselt dann für den Autofokus auf L und danach " +
                "zurück; der NSTN-Filterplan läuft mit seinem Filter weiter. Die Fokus-Offsets der anderen Filter relativ zu L eintragen.",
                "Läuft, sobald der Filterplan beendet ist: In dieser Nacht öffnet kein NSTN-Fenster mehr."),
        };

        private static ISequenceRootContainer WriteShareable(string path, IProfileService profile, SequenceJsonConverter converter, NstnFilterPlan template,
            (string Start, string Target, string Focus, string End) notes) {
            // The plan builds "Switch filter" from the profile's list: a filter wheel with the plan's filters.
            var filters = profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters;
            filters.Clear();
            foreach (var (filter, position) in new[] { "L", "R", "G", "B", "Ha" }.Select((f, i) => (f, i))) {
                filters.Add(new FilterInfo(filter, 0, (short)position));
            }

            var start = Named(new StartAreaContainer());
            start.Add(Note(notes.Start));
            var cool = Named(Construct<CoolCamera>(profile));
            cool.Temperature = -10;
            start.Add(cool);
            start.Add(Named(Construct<UnparkScope>(profile)));

            var dso = Named(Construct<DeepSkyObjectContainer>(profile));
            dso.Name = "M 33";
            dso.Target = new InputTarget(Angle.ByDegree(0), Angle.ByDegree(0), null) {
                TargetName = "M 33",
                // J2000 (SIMBAD): 01h 33m 50.02s, +30° 39′ 36.7″
                InputCoordinates = new InputCoordinates(new Coordinates(
                    Angle.ByHours(1 + 33 / 60.0 + 50.02 / 3600.0), Angle.ByDegree(30 + 39 / 60.0 + 36.7 / 3600.0), Epoch.J2000)),
            };
            dso.Add(Named(Construct<MeridianFlipTrigger>(profile)));
            var dither = Named(Construct<DitherAfterExposures>(profile));
            dither.AfterExposures = 3;
            dso.Add(dither);
            // Autofocus every hour on the profile's autofocus filter (L). N.I.N.A. switches to
            // that filter and back on its own (AutoFocusVM.SetAutofocusFilter, "use filter
            // wheel offsets"); a "Switch filter" in the trigger would leave the rest of a
            // filter plan block on L.
            var autofocus = Named(Construct<AutofocusAfterTimeTrigger>(profile));
            autofocus.Amount = 60;
            dso.Add(autofocus);

            dso.Add(Note(notes.Target));
            var info = Named(new NstnNightInfo(profile));
            info.Name = Text.T("Name_NightInfo");
            dso.Add(info);
            // A real calculator: with a stub that fails, WaitForTime recurses between UpdateTime and Validate.
            var nauticalDusk = new NauticalDuskProvider(new NighttimeCalculator(profile));
            dso.Add(Named(new WaitForTime(new List<IDateTimeProvider> { nauticalDusk }, nauticalDusk)));
            var altitude = Named(Construct<WaitForAltitude>(profile));
            altitude.Data.Offset = 20;
            dso.Add(altitude);
            dso.Add(Named(Construct<Center>(profile)));
            // Focus once before guiding starts; from then on the trigger above, every hour.
            dso.Add(Note(notes.Focus));
            dso.Add(Named(Construct<RunAutofocus>(profile)));
            dso.Add(Named(Construct<StartGuiding>(profile)));
            var plan = (NstnFilterPlan)template.Clone();
            plan.Name = Text.T("Name_Plan");
            plan.Rows.Clear();
            foreach (var filter in new[] { "L", "R", "G", "B" }) {
                plan.Rows.Add(new FilterPlanRow { FilterName = filter, Count = 5, ExposureSeconds = 120 });
            }
            plan.Rows.Add(new FilterPlanRow { FilterName = "Ha", Count = 5, ExposureSeconds = 300 });
            plan.RoundsPerFilter = 2;
            dso.Add(plan);

            var targets = Named(new TargetAreaContainer());
            targets.Add(dso);

            var end = Named(new EndAreaContainer());
            end.Add(Note(notes.End));
            end.Add(Named(Construct<StopGuiding>(profile)));
            end.Add(Named(Construct<WarmCamera>(profile)));
            end.Add(Named(Construct<ParkScope>(profile)));

            var root = Named(new SequenceRootContainer());
            root.Name = "NSTN LRGB + Ha";
            root.Add(start);
            root.Add(targets);
            root.Add(end);

            File.WriteAllText(path, converter.Serialize(root));
            Console.WriteLine($"written: {path} ({plan.ActualStructure})");
            return root;
        }

        /// <summary>
        /// Loads the written file the way N.I.N.A. does (factory templates of every type in
        /// it, clone, populate) and serializes it again. Returns the problems: types the
        /// factory did not know (they would load as "Unknown") and any difference to the file.
        /// </summary>
        public static List<string> RoundTrip(string path, IProfileService profile, ISequenceRootContainer written, NstnFilterPlan template) {
            // Every type in the file, the runners of the triggers included.
            var types = new HashSet<Type>();
            IDateTimeProvider provider = null;
            void Collect(ISequenceContainer container) {
                types.Add(container.GetType());
                foreach (var condition in (container as IConditionable)?.GetConditionsSnapshot() ?? new List<ISequenceCondition>()) {
                    types.Add(condition.GetType());
                }
                foreach (var trigger in (container as ITriggerable)?.GetTriggersSnapshot() ?? new List<ISequenceTrigger>()) {
                    types.Add(trigger.GetType());
                    if (trigger is SequenceTrigger { TriggerRunner: not null } withRunner) {
                        Collect(withRunner.TriggerRunner);
                    }
                }
                foreach (var item in container.GetItemsSnapshot()) {
                    if (item is ISequenceContainer child) {
                        Collect(child);
                    } else {
                        types.Add(item.GetType());
                        provider ??= (item as WaitForTime)?.SelectedProvider;
                    }
                }
            }
            Collect(written);
            // Factory templates as MEF builds them: fresh, unconfigured, with a name (the
            // factory's sidebar filter reads every name). Clones of the configured entities
            // would share objects such as FilterInfo, which Newtonsoft then refuses to reuse.
            var providers = new List<IDateTimeProvider> { provider };
            var fresh = types.Select(t => {
                var entity = t == typeof(NstnFilterPlan) ? (ISequenceEntity)template.Clone()
                    : t == typeof(WaitForTime) ? new WaitForTime(providers)
                    : Construct(t, profile);
                entity.Name ??= t.Name;
                return entity;
            }).ToList();
            var factory = new SequencerFactory(profile,
                fresh.OfType<ISequenceItem>().Where(e => e is not ISequenceContainer).ToList(),
                fresh.OfType<ISequenceCondition>().ToList(),
                fresh.OfType<ISequenceTrigger>().ToList(),
                fresh.OfType<ISequenceContainer>().ToList(),
                providers);
            var converter = new SequenceJsonConverter(factory);

            var json = File.ReadAllText(path);
            var loaded = converter.Deserialize(json);
            var problems = new List<string>();
            var unknown = new List<string>();
            void Find(ISequenceContainer container) {
                foreach (var entity in new ISequenceEntity[] { container }
                    .Concat((container as IConditionable)?.GetConditionsSnapshot() ?? new List<ISequenceCondition>())
                    .Concat((container as ITriggerable)?.GetTriggersSnapshot() ?? new List<ISequenceTrigger>())
                    .Concat(container.GetItemsSnapshot().Where(i => i is not ISequenceContainer))) {
                    if (entity.GetType().Name.StartsWith("Unknown", StringComparison.Ordinal)) {
                        unknown.Add(entity.GetType().Name);
                    }
                }
                foreach (var child in container.GetItemsSnapshot().OfType<ISequenceContainer>()) {
                    Find(child);
                }
                foreach (var trigger in ((container as ITriggerable)?.GetTriggersSnapshot() ?? new List<ISequenceTrigger>()).OfType<SequenceTrigger>()) {
                    if (trigger.TriggerRunner != null) {
                        Find(trigger.TriggerRunner);
                    }
                }
            }
            Find(loaded);
            if (unknown.Count > 0) {
                problems.Add("unknown after load: " + string.Join(", ", unknown));
            }
            var again = converter.Serialize(loaded);
            if (again != json) {
                var a = json.Split('\n');
                var b = again.Split('\n');
                var line = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], Math.Min(a.Length, b.Length));
                problems.Add($"saved again differs from line {line + 1}: '{(line < a.Length ? a[line].Trim() : "")}' vs '{(line < b.Length ? b[line].Trim() : "")}'");
            }
            return problems;
        }

        private static Annotation Note(string text) => Named(new Annotation { Text = text });

        /// <summary>What N.I.N.A.'s plugin loader does after constructing: the name from the export metadata.</summary>
        private static T Named<T>(T entity) where T : ISequenceEntity {
            var name = typeof(T).GetCustomAttributes<ExportMetadataAttribute>().FirstOrDefault(m => m.Name == "Name")?.Value as string;
            if (name != null) {
                entity.Name = name.StartsWith("Lbl_", StringComparison.Ordinal) ? Loc.Instance[name] : name;
            }
            return entity;
        }

        /// <summary>What MEF does: the importing constructor, every interface a stub.</summary>
        internal static T Construct<T>(IProfileService profile) => (T)Construct(typeof(T), profile);

        /// <summary>As above; a type without an importing constructor (the containers) takes its parameterless one.</summary>
        private static ISequenceEntity Construct(Type type, IProfileService profile) {
            var ctor = type.GetConstructors().FirstOrDefault(c => c.GetCustomAttribute<ImportingConstructorAttribute>() != null);
            if (ctor == null) {
                return (ISequenceEntity)Activator.CreateInstance(type);
            }
            var args = ctor.GetParameters()
                .Select(p => p.ParameterType == typeof(IProfileService) ? profile : p.ParameterType.IsInterface ? Stub.Create(p.ParameterType) : null)
                .ToArray();
            return (ISequenceEntity)ctor.Invoke(args);
        }
    }
}
