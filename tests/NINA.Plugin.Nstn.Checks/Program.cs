using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NINA.Core.Enum;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Nstn.Conditions;
using NINA.Plugin.Nstn.FilterPlan;
using NINA.Plugin.Nstn.Instructions;
using NINA.Profile.Interfaces;
using NINA.Sequencer;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.DragDrop;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.Serialization;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using Nstn.Core;

namespace NINA.Plugin.Nstn.Checks {

    /// <summary>
    /// Checks of the filter plan against N.I.N.A.'s own sequencer classes, which the
    /// unit tests (Nstn.Core.Tests, Linux container) cannot load: the factory that
    /// loading uses, the JSON converter, the drop command of a container, the template
    /// wrapper, the sequencer's pre-start validation and SequentialStrategy with the real
    /// triggers. Runs on Windows against the DLLs of the installed N.I.N.A.
    /// (tests/Run-NinaChecks.ps1). Devices are stubs; the profile has the filters of the
    /// test profile (Askar D1, Ha, L).
    ///
    ///   NINA.Plugin.Nstn.Checks.exe [out] [--no-runs]          all checks, exit code 1 on a failure
    ///   NINA.Plugin.Nstn.Checks.exe out write-golden file       the golden sequence (after a deliberate format change)
    ///   NINA.Plugin.Nstn.Checks.exe out write-flip-sequence file  the sequence for the acceptance run in N.I.N.A.
    /// </summary>
    public static class Program {

        private static int failed;
        private static int passed;

        private static IProfileService profile;
        private static ICameraMediator camera;
        private static IImagingMediator imaging;
        private static IImageSaveMediator imageSave;
        private static IImageHistoryVM history;
        private static IFilterWheelMediator filterWheel;
        private static NstnFilterPlan template;
        private static SequencerFactory factory;
        private static SequenceJsonConverter converter;
        private static string outDir;

        [STAThread]
        public static int Main(string[] args) {
            var runs = !args.Contains("--no-runs");
            args = args.Where(a => a != "--no-runs").ToArray();
            outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "run");
            Directory.CreateDirectory(outDir);
            // N.I.N.A.'s logger writes below this path on first use: keep it out of the real profile.
            CoreUtil.APPLICATIONTEMPPATH = Path.Combine(outDir, "nina-appdata");

            var sequencerDll = typeof(SequenceContainer).Assembly.Location;
            Console.WriteLine($"NINA.Sequencer {typeof(SequenceContainer).Assembly.GetName().Version} {Sha256(sequencerDll)}");

            Setup();
            if (args.Length > 2 && args[1] == "write-golden") {
                File.WriteAllText(args[2], GoldenSequence());
                Console.WriteLine($"written: {args[2]}");
                Logger.CloseAndFlush();
                return 0;
            }
            if (args.Length > 2 && args[1] == "write-flip-sequence") {
                SequenceWriter.WriteFlipTest(args[2], profile, converter, template);
                Logger.CloseAndFlush();
                return 0;
            }
            try {
                CloneChecks();
                DropAndTemplateChecks();
                SaveLoadChecks();
                ValidateChecks();
                FailureChecks();
                GoldenChecks();
                if (runs) {
                    RunChecks();
                }
            } catch (Exception ex) {
                Fail("unexpected exception", ex.ToString());
            }
            Logger.CloseAndFlush();
            Console.WriteLine();
            Console.WriteLine($"{passed} passed, {failed} failed");
            return failed == 0 ? 0 : 1;
        }

        // -------------------------------------------------------------------

        private static void Setup() {
            profile = Stub.Create<IProfileService>();
            var filters = profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters;
            filters.Add(new FilterInfo("Askar D1", 0, 0));
            filters.Add(new FilterInfo("Ha", 0, 1));
            filters.Add(new FilterInfo("L", 0, 2));
            camera = Stub.Create<ICameraMediator>();
            imaging = Stub.Create<IImagingMediator>();
            imageSave = Stub.Create<IImageSaveMediator>();
            history = Stub.Create<IImageHistoryVM>();
            filterWheel = Stub.Create<IFilterWheelMediator>();

            // What MEF and the plugin loader do: construct, then name from the export metadata.
            template = new NstnFilterPlan(profile, camera, imaging, imageSave, history, filterWheel) {
                Name = "NSTN filter plan",
                Category = "NSTN",
            };
            var items = new List<ISequenceItem> {
                new SequentialContainer { Name = "Sequential" },
                new TakeExposure(profile, camera, imaging, imageSave, history),
                new SwitchFilter(profile, filterWheel),
                new NstnWaitForWindow(profile),
                template,
            };
            var conditions = new List<ISequenceCondition> { new LoopCondition(), new NstnWindowCondition(profile), new NstnNightLeftCondition(profile) };
            var containers = new List<ISequenceContainer> { new SequentialContainer(), template };
            foreach (var entity in items.Cast<ISequenceEntity>().Concat(conditions).Concat(containers)) {
                entity.Name ??= entity.GetType().Name;
            }
            factory = new SequencerFactory(profile, items, conditions, new List<ISequenceTrigger>(), containers, new List<IDateTimeProvider>());
            converter = new SequenceJsonConverter(factory);

            Check("default rows from the profile", Rows(template) == "Askar D1 5x300, Ha 5x300, L 5x300", Rows(template));
            Check("template children match its rows", template.ActualStructure == template.ExpectedStructure, template.ActualStructure);
        }

        // -------------------------------------------------------------------
        // 1. Clone(): drag and drop, templates
        // -------------------------------------------------------------------

        private static void CloneChecks() {
            Console.WriteLine();
            Console.WriteLine("== 1. Clone ==");
            var source = (NstnFilterPlan)factory.GetContainer<NstnFilterPlan>();
            source.Rows[1].Count = 7;
            source.Rows[2].FilterName = "Askar D1";
            source.Rows[2].ExposureSeconds = 120;
            source.Rows.Add(new FilterPlanRow { FilterName = "L", Count = 10, ExposureSeconds = 60 });
            Check("edited rows rebuild the children", Plain(source.ActualStructure) == "Wait | Askar D1 5x300 | Ha 7x300 | Askar D1 5x120 | L 10x60 | End", source.ActualStructure);
            Check("default plan values (camera gain/offset, 1x1, round robin)", source.ActualStructure.Contains("Ha 7x300 g-1 o-1 1x1 r1"), source.ActualStructure);
            source.Gain = 100;
            source.Offset = 30;
            source.Binning = new BinningMode(2, 2);
            source.RoundsPerFilter = 2;
            source.StopAfterFailures = 4;
            Check("stop after failures does not rebuild", source.ActualStructure == source.ExpectedStructure, source.ActualStructure);
            source.StopAfterFailures = -1;
            Check("negative stop after failures is refused", source.StopAfterFailures == 4, $"{source.StopAfterFailures}");
            Check("plan values reach every block", Regex.Matches(source.ActualStructure, " g100 o30 2x2 r2").Count == 4 && source.ActualStructure == source.ExpectedStructure, source.ActualStructure);
            Check("rounds per filter = loop count of the block", ((LoopCondition)((SequentialContainer)source.Items[1]).Conditions[1]).Iterations == 2, "");
            source.Gain = -5;
            source.RoundsPerFilter = 0;
            Check("invalid plan values are refused", source.Gain == 100 && source.RoundsPerFilter == 2, $"gain {source.Gain}, rounds {source.RoundsPerFilter}");

            var clone = (NstnFilterPlan)source.Clone();
            SameCopy("Clone()", source, clone);

            clone.Rows[0].Count = 2;
            Check("editing the clone leaves the source alone", source.Rows[0].Count == 5 && source.ActualStructure.Contains("Askar D1 5x300"), source.ActualStructure);
            Check("editing the clone rebuilds the clone", clone.ActualStructure.StartsWith("Wait | Askar D1 2x300"), clone.ActualStructure);
        }

        private static void DropAndTemplateChecks() {
            Console.WriteLine();
            Console.WriteLine("== 1. Drag and drop, template (N.I.N.A.'s drop command) ==");
            var target = new SequentialContainer { Name = "Target" };

            // From the instruction list: the source is the factory's template, no parent.
            target.DropIntoCommand.Execute(new DropIntoParameters(template, target, DropTargetEnum.Center));
            var dropped = target.Items.OfType<NstnFilterPlan>().LastOrDefault();
            Check("drop from the list inserts a copy", dropped != null && !ReferenceEquals(dropped, template) && dropped.Parent == target, Describe(dropped));
            if (dropped != null) {
                SameCopy("drop from the list", template, dropped);
            }

            // Ctrl+drag inside the sequence: duplicate of a plan that has a parent.
            dropped.Rows[0].Count = 3;
            target.DropIntoCommand.Execute(new DropIntoParameters(dropped, target, DropTargetEnum.Center) { Duplicate = true });
            var duplicate = target.Items.OfType<NstnFilterPlan>().LastOrDefault();
            Check("duplicate by drag inserts a second plan", target.Items.OfType<NstnFilterPlan>().Count() == 2 && !ReferenceEquals(duplicate, dropped), $"{target.Items.Count} items");
            SameCopy("duplicate by drag", dropped, duplicate);

            // Template library: TemplateController saves with the converter and loads with it;
            // dropping a template calls TemplatedSequenceContainer.Clone().
            var json = converter.Serialize(dropped);
            File.WriteAllText(Path.Combine(outDir, "filterplan.template.json"), json);
            var stored = (ISequenceContainer)converter.Deserialize(json);
            var wrapper = new TemplatedSequenceContainer(profile, "Lbl_SequenceTemplateController_UserTemplatesGroupName", stored);
            Check("template file loads as a plan", stored is NstnFilterPlan, stored?.GetType().Name);
            target.DropIntoCommand.Execute(new DropIntoParameters(wrapper, target, DropTargetEnum.Center));
            var fromTemplate = target.Items.OfType<NstnFilterPlan>().LastOrDefault();
            Check("drop of the template inserts a third plan", target.Items.OfType<NstnFilterPlan>().Count() == 3 && !ReferenceEquals(fromTemplate, stored), $"{target.Items.Count} items");
            SameCopy("template drop", dropped, fromTemplate);
        }

        // -------------------------------------------------------------------
        // 2. Save and load
        // -------------------------------------------------------------------

        private static void SaveLoadChecks() {
            Console.WriteLine();
            Console.WriteLine("== 2. Save and load (SequenceJsonConverter, factory template -> Clone -> Populate) ==");
            var root = new SequentialContainer { Name = "Root" };
            var plan = (NstnFilterPlan)factory.GetContainer<NstnFilterPlan>();
            plan.Rows[1].Count = 7;
            plan.Rows[2].ExposureSeconds = 60;
            root.Add(plan);
            plan.Gain = 123;
            plan.Offset = 12;
            plan.Binning = new BinningMode(2, 2);
            plan.RoundsPerFilter = 3;
            plan.StopAfterFailures = 7;
            // A child value the table does not know: survives only if the loaded children are kept.
            FirstExposure(plan).ImageType = "DARK";

            var json1 = converter.Serialize(root);
            File.WriteAllText(Path.Combine(outDir, "roundtrip-1.json"), json1);
            var loaded = (ISequenceContainer)converter.Deserialize(json1);
            var loadedPlan = loaded.Items.OfType<NstnFilterPlan>().SingleOrDefault();
            Check("plan comes back as a plan", loadedPlan != null, Describe(loaded));
            if (loadedPlan == null) {
                return;
            }
            Check("rows restored", Rows(loadedPlan) == Rows(plan), Rows(loadedPlan));
            Check("children restored and match the rows", loadedPlan.ActualStructure == plan.ActualStructure && loadedPlan.ActualStructure == loadedPlan.ExpectedStructure, loadedPlan.ActualStructure);
            Check("stop after failures restored", loadedPlan.StopAfterFailures == 7, $"{loadedPlan.StopAfterFailures}");
            Check("plan values restored", loadedPlan.Gain == 123 && loadedPlan.Offset == 12 && Equals(loadedPlan.Binning, new BinningMode(2, 2)) && loadedPlan.RoundsPerFilter == 3,
                $"g{loadedPlan.Gain} o{loadedPlan.Offset} {loadedPlan.Binning} r{loadedPlan.RoundsPerFilter}");
            Check("loaded children are the file's, not rebuilt (image type DARK kept)", FirstExposure(loadedPlan).ImageType == "DARK", FirstExposure(loadedPlan).ImageType);
            Check("parents restored", ParentsConsistent(loadedPlan) && loadedPlan.Parent == loaded, "");
            Check("no duplicate rows or children from the factory clone", loadedPlan.Rows.Count == 3 && loadedPlan.Items.Count == 4, $"{loadedPlan.Rows.Count} rows, {loadedPlan.Items.Count} items");

            var json2 = converter.Serialize(loaded);
            File.WriteAllText(Path.Combine(outDir, "roundtrip-2.json"), json2);
            Check("save -> load -> save gives the same file", json1 == json2, $"{json1.Length} / {json2.Length} chars");

            // Hand-edited file: the children say 99 exposures, the row says 5.
            var edited = JObject.Parse(json1);
            var series = edited.Descendants().OfType<JObject>()
                .First(o => o["Strategy"] != null && (string)o["Name"] == "5 × 300 s");
            series["Conditions"]["$values"][0]["Iterations"] = 99;
            var tampered = (ISequenceContainer)converter.Deserialize(edited.ToString());
            var tamperedPlan = tampered.Items.OfType<NstnFilterPlan>().Single();
            Check("children that contradict the rows are rebuilt from the rows", tamperedPlan.ActualStructure == tamperedPlan.ExpectedStructure && tamperedPlan.ActualStructure.Contains("Askar D1 5x300"), tamperedPlan.ActualStructure);
            Check("... and lose what only the children had (image type)", FirstExposure(tamperedPlan).ImageType == "LIGHT", FirstExposure(tamperedPlan).ImageType);
            Check("... and keep the plan values", tamperedPlan.ActualStructure.Contains(" g123 o12 2x2 r3"), tamperedPlan.ActualStructure);

            // Hand-edited file: one exposure has another gain than the plan.
            var otherGain = JObject.Parse(json1);
            otherGain.Descendants().OfType<JObject>().First(o => o["Gain"] != null && o["ExposureTime"] != null)["Gain"] = 50;
            var otherGainPlan = ((ISequenceContainer)converter.Deserialize(otherGain.ToString())).Items.OfType<NstnFilterPlan>().Single();
            Check("an exposure with another gain than the plan is rebuilt", FirstExposure(otherGainPlan).Gain == 123 && otherGainPlan.ActualStructure == otherGainPlan.ExpectedStructure, otherGainPlan.ActualStructure);

            // A table edit after loading.
            loaded.ClearHasChanged();
            var oldBlock = loadedPlan.Items[2];
            loadedPlan.Rows[1].Count = 3;
            Check("table edit rebuilds the children", loadedPlan.ActualStructure.Contains("Ha 3x300") && !loadedPlan.Items.Contains(oldBlock) && oldBlock.Parent == null, loadedPlan.ActualStructure);
            Check("table edit marks the sequence as changed", loaded.HasChanged, "HasChanged");
            loaded.ClearHasChanged();
            loadedPlan.RoundsPerFilter = 1;
            Check("plan value edit rebuilds and marks the sequence as changed", loaded.HasChanged && loadedPlan.ActualStructure.Contains("Ha 3x300 g123 o12 2x2 r1"), loadedPlan.ActualStructure);
            loadedPlan.Rows.RemoveAt(0);
            Check("removing a row removes its block", Plain(loadedPlan.ActualStructure) == "Wait | Ha 3x300 | L 5x60 | End", loadedPlan.ActualStructure);
            loadedPlan.AddRowCommand.Execute(null);
            Check("adding a row adds a block (first unused filter)", Plain(loadedPlan.ActualStructure) == "Wait | Ha 3x300 | L 5x60 | Askar D1 5x300 | End", loadedPlan.ActualStructure);
        }

        // -------------------------------------------------------------------
        // Stop after N failed exposures in a row
        // -------------------------------------------------------------------

        /// <summary>One run of an exposure as N.I.N.A. reports it: RUNNING, then the outcome.</summary>
        private static void RunOnce(TakeExposure exposure, SequenceEntityStatus outcome, int times = 1) {
            for (var i = 0; i < times; i++) {
                exposure.Status = SequenceEntityStatus.RUNNING;
                exposure.Status = outcome;
            }
        }

        /// <summary>The end condition's check before the next item; true if it ended the plan for failures.</summary>
        private static bool Stopped(NstnNightLeftCondition end, ISequenceItem item) {
            var result = end.Check(item, item);
            return !result && end.Summary.StartsWith("Ended:");
        }

        private static void FailureChecks() {
            Console.WriteLine();
            Console.WriteLine("== Stop after failed exposures in a row ==");
            var root = new SequentialContainer { Name = "Root" };
            var plan = (NstnFilterPlan)factory.GetContainer<NstnFilterPlan>();
            root.Add(plan);
            var end = (NstnNightLeftCondition)plan.Conditions[0];
            var first = FirstExposure(plan);
            var second = (TakeExposure)((ISequenceContainer)((ISequenceContainer)plan.Items[2]).Items[1]).Items[0];
            Check("default: stop after 3", plan.StopAfterFailures == 3, $"{plan.StopAfterFailures}");

            RunOnce(first, SequenceEntityStatus.FAILED);
            RunOnce(second, SequenceEntityStatus.FAILED);
            Check("2 failures: not stopped", !Stopped(end, second), end.Summary);
            RunOnce(first, SequenceEntityStatus.FAILED);
            Check("3 failures in a row (across blocks): the plan ends", Stopped(end, first), end.Summary);

            end.SequenceBlockInitialize();
            Check("a new run of the plan counts again", !Stopped(end, first), end.Summary);

            RunOnce(first, SequenceEntityStatus.FAILED, 2);
            RunOnce(second, SequenceEntityStatus.FINISHED);
            RunOnce(first, SequenceEntityStatus.FAILED, 2);
            Check("a good exposure resets the count", !Stopped(end, first), end.Summary);
            RunOnce(first, SequenceEntityStatus.FAILED);
            Check("... and counting goes on from there", Stopped(end, first), end.Summary);

            // A failed validation: N.I.N.A. 3.2 sets FAILED twice for one exposure.
            end.SequenceBlockInitialize();
            for (var i = 0; i < 2; i++) {
                first.Status = SequenceEntityStatus.RUNNING;
                first.Status = SequenceEntityStatus.FAILED;
                first.Status = SequenceEntityStatus.FAILED;
            }
            Check("an exposure that fails its validation counts once (N.I.N.A. sets FAILED twice)", !Stopped(end, first), end.Summary);
            RunOnce(first, SequenceEntityStatus.FAILED);
            Check("... so the third such exposure ends the plan", Stopped(end, first) && end.Summary.StartsWith("Ended: 3"), end.Summary);

            end.SequenceBlockInitialize();
            plan.StopAfterFailures = 0;
            RunOnce(first, SequenceEntityStatus.FAILED, 10);
            Check("0 = never stop", !Stopped(end, first), end.Summary);

            // After a rebuild (row edited) the new exposures are watched, the old ones not.
            plan.StopAfterFailures = 3;
            end.SequenceBlockInitialize();
            plan.Rows[0].Count = 4;
            end = (NstnNightLeftCondition)plan.Conditions[0];
            var rebuilt = FirstExposure(plan);
            RunOnce(first, SequenceEntityStatus.FAILED, 3);
            Check("exposures dropped by a rebuild are no longer counted", !Stopped(end, rebuilt), end.Summary);
            RunOnce(rebuilt, SequenceEntityStatus.FAILED, 3);
            Check("the rebuilt exposures are counted", Stopped(end, rebuilt), end.Summary);

            // A loaded plan (children from the file) is watched as well.
            end.SequenceBlockInitialize();
            var loaded = (ISequenceContainer)converter.Deserialize(converter.Serialize(root));
            var loadedPlan = loaded.Items.OfType<NstnFilterPlan>().Single();
            var loadedEnd = (NstnNightLeftCondition)loadedPlan.Conditions[0];
            var loadedExposure = FirstExposure(loadedPlan);
            Check("a loaded plan starts at zero", !Stopped(loadedEnd, loadedExposure), loadedEnd.Summary);
            RunOnce(loadedExposure, SequenceEntityStatus.FAILED, 3);
            Check("a loaded plan counts its file's exposures", Stopped(loadedEnd, loadedExposure), loadedEnd.Summary);
        }

        // -------------------------------------------------------------------
        // Golden file: a sequence saved by this version must load unchanged
        // -------------------------------------------------------------------

        private static string GoldenPath() => Path.Combine(AppContext.BaseDirectory, "golden", "filterplan-3.2.json");

        /// <summary>A plan with every value off its default, inside a root, as N.I.N.A. saves it.</summary>
        private static string GoldenSequence() {
            var root = new SequentialContainer { Name = "Root" };
            var plan = (NstnFilterPlan)factory.GetContainer<NstnFilterPlan>();
            plan.Rows[1].Count = 7;
            plan.Rows[2].ExposureSeconds = 60;
            plan.Gain = 123;
            plan.Offset = 12;
            plan.Binning = new BinningMode(2, 2);
            plan.RoundsPerFilter = 3;
            plan.StopAfterFailures = 7;
            root.Add(plan);
            // A value only the children have: it survives only if the loaded children are kept.
            FirstExposure(plan).ImageType = "DARK";
            return converter.Serialize(root);
        }

        private static void GoldenChecks() {
            Console.WriteLine();
            Console.WriteLine("== Golden file (a 3.2 sequence saved by this plugin) ==");
            var golden = File.ReadAllText(GoldenPath());
            var loaded = (ISequenceContainer)converter.Deserialize(golden);
            var plan = loaded.Items.OfType<NstnFilterPlan>().SingleOrDefault();
            Check("golden: the plan loads", plan != null, Describe(loaded));
            if (plan == null) {
                return;
            }
            Check("golden: rows", Rows(plan) == "Askar D1 5x300, Ha 7x300, L 5x60", Rows(plan));
            Check("golden: plan values", plan.Gain == 123 && plan.Offset == 12 && Equals(plan.Binning, new BinningMode(2, 2)) && plan.RoundsPerFilter == 3 && plan.StopAfterFailures == 7,
                $"g{plan.Gain} o{plan.Offset} {plan.Binning} r{plan.RoundsPerFilter} s{plan.StopAfterFailures}");
            Check("golden: the file's children are kept (image type DARK)", FirstExposure(plan).ImageType == "DARK" && plan.ActualStructure == plan.ExpectedStructure, plan.ActualStructure);
            Check("golden: saving it again gives the same file", converter.Serialize(loaded) == golden, "format changed - if on purpose, write a new golden file");
            Check("golden: this version still writes the same file", GoldenSequence() == golden, "format changed - if on purpose, write a new golden file");
        }

        // -------------------------------------------------------------------
        // Runs with N.I.N.A.'s SequentialStrategy and stub devices
        // -------------------------------------------------------------------

        /// <summary>An instant of the night 18/19 Nov 2026 in Kordel, given as CET (UTC+1).</summary>
        private static DateTime Night(int hour, int minute, int second) =>
            new DateTime(2026, 11, 19, hour, minute, second, DateTimeKind.Utc).AddHours(-1);

        private static void RunChecks() {
            Console.WriteLine();
            Console.WriteLine("== Runs (SequentialStrategy, real triggers, stub devices, NSTN clock in the night 18/19 Nov) ==");
            if (TimeZoneInfo.Local.GetUtcOffset(Night(5, 40, 0)) != TimeSpan.FromHours(1)) {
                Console.WriteLine("  note: this machine is not on CET; the windows below are for Kordel in CET");
            }

            // 05:40: O III and H-alpha open, L/RGB closed.
            var a = PlanRun.Execute(Path.Combine(outDir, "run-a"), Night(5, 40, 0), TimeSpan.FromSeconds(20), roundsPerFilter: 1, imagesMissing: false);
            a.WriteTimeline(Path.Combine(outDir, "run-a.txt"));
            Console.WriteLine($"  run A (round robin): {a.Captures} exposures, {a.FilterChanges} filter switches, {a.Autofocus} AF, {a.Dither} dither, {a.Probes} probes");
            Check("run A: exposures taken", a.Captures >= 6, $"{a.Captures}");
            Check("run A: the target's triggers are checked before every exposure", a.Probes == a.Captures, $"{a.Probes} probes, {a.Captures} exposures");
            Check("run A: dither after every exposure", Math.Abs(a.Dither - a.Captures) <= 1, $"{a.Dither} dither, {a.Captures} exposures");
            Check("run A: AF after every filter change", a.Autofocus >= 2 && a.Autofocus >= a.FilterChanges - 1, $"{a.Autofocus} AF, {a.FilterChanges} switches");
            Check("run A: both open filters exposed, L/RGB not (its window closed at 05:26)",
                a.CapturesPerFilter.ContainsKey("Askar D1") && a.CapturesPerFilter.ContainsKey("Ha") && !a.CapturesPerFilter.ContainsKey("L"),
                string.Join(", ", a.CapturesPerFilter.Select(kv => $"{kv.Key} {kv.Value}")));

            // The view: the night plan of the hidden waiting instruction and the window of each row.
            Console.WriteLine($"  night plan: {a.NightPlan.Summary}");
            foreach (var row in a.NightPlan.Rows) {
                Console.WriteLine($"    {row.Label,-24} {row.Expected}");
            }
            foreach (var shown in a.WindowsShown) {
                Console.WriteLine($"    window {shown}");
            }
            Check("view: the night plan has a row per filter row", a.NightPlan.HasPlan && a.NightPlan.Rows.Select(r => r.Label).SequenceEqual(a.Plan.Rows.Select(r => r.Label)),
                string.Join(" / ", a.NightPlan.Rows.Select(r => r.Label)));
            Check("view: the plan's end condition is understood (no 'not part of the estimate' note)", !a.NightPlan.Notes.Contains("not part of the estimate") && !a.NightPlan.Notes.Contains("runs once"), a.NightPlan.Notes);
            Check("view: the night plan ends when no window opens again", a.NightPlan.Summary.EndsWith("no window opens again"), a.NightPlan.Summary);
            Check("view: H-alpha gets time until 06:10, L/RGB none", a.NightPlan.Rows[1].Expected != a.NightPlan.Rows[2].Expected && a.NightPlan.Rows[2].Expected == Text.T("Plan_Nothing"),
                string.Join(" / ", a.NightPlan.Rows.Select(r => r.Expected)));
            Check("view: one window line per row, with tonight's window and the state now", a.WindowsShown.Count == 3 && a.WindowsShown.All(w => w.Contains("Tonight") && (w.Contains("Open") || w.Contains("Closed"))),
                string.Join(" / ", a.WindowsShown));
            Check("view: the waiting instruction is the plan's first child", ReferenceEquals(a.Plan.Waiter, a.Plan.Items[0]), a.Plan.Waiter?.GetType().Name);
            Check("view: the rows in one line for the imaging tab", a.Plan.RowsText == "Askar D1: 2 × 1 s · Ha: 2 × 1 s · L: 2 × 1 s", a.Plan.RowsText);

            var b = PlanRun.Execute(Path.Combine(outDir, "run-b"), Night(5, 40, 0), TimeSpan.FromSeconds(20), roundsPerFilter: 3, imagesMissing: false);
            b.WriteTimeline(Path.Combine(outDir, "run-b.txt"));
            Console.WriteLine($"  run B (3 rounds per filter): {b.Captures} exposures, {b.FilterChanges} filter switches, {b.Autofocus} AF");
            // Night Info's sequence check on the real tree: target with Meridian Flip, Dither, AF after filter change.
            foreach (var finding in a.Findings.Concat(b.Findings).Distinct()) {
                Console.WriteLine($"    check: {finding}");
            }
            Check("sequence check: round robin with AF after filter change is pointed out (real trigger types)",
                a.Findings.Count(f => f.StartsWith("Hint:") && f.Contains("changes the filter in every round")) == 1, string.Join(" / ", a.Findings));
            Check("sequence check: ... not with 3 rounds per filter", !b.Findings.Any(f => f.Contains("changes the filter in every round")), string.Join(" / ", b.Findings));
            Check("sequence check: the plan follows the NSTN pattern (no warnings)", !a.Findings.Concat(b.Findings).Any(f => f.StartsWith("Warning:")),
                string.Join(" / ", a.Findings.Concat(b.Findings)));
            Check("run B: 3 rounds per filter at least halve the AF runs per exposure",
                b.Captures > 0 && a.Captures > 0 && 2.0 * b.Autofocus / b.Captures <= (double)a.Autofocus / a.Captures,
                $"A {a.Autofocus}/{a.Captures}, B {b.Autofocus}/{b.Captures}");

            var c = PlanRun.Execute(Path.Combine(outDir, "run-c"), Night(5, 40, 0), TimeSpan.FromSeconds(30), roundsPerFilter: 1, imagesMissing: true);
            c.WriteTimeline(Path.Combine(outDir, "run-c.txt"));
            Console.WriteLine($"  run C (image folder missing): {c.Captures} exposures, {c.Autofocus} AF, ended after {c.Duration.TotalSeconds:0.0} s, {c.Plan.FailedInARow} failed in a row");
            Check("run C: no exposure is taken", c.Captures == 0, $"{c.Captures}");
            Check("run C: the plan ends by itself after 3 failed exposures", !c.TimedOut && c.Plan.FailedInARow == 3 && c.End.Summary.StartsWith("Ended:"), c.End.Summary);
            Check("run C: ... within seconds, not after a night of filter changes", c.Duration < TimeSpan.FromSeconds(15), $"{c.Duration.TotalSeconds:0.0} s");

            // 06:09:40: only H-alpha is left, until 06:10.
            var d = PlanRun.Execute(Path.Combine(outDir, "run-d"), Night(6, 9, 40), TimeSpan.FromSeconds(90), roundsPerFilter: 1, imagesMissing: false);
            d.WriteTimeline(Path.Combine(outDir, "run-d.txt"));
            Console.WriteLine($"  run D (end of night): {d.Captures} exposures, ended after {d.Duration.TotalSeconds:0.0} s: {d.End.Summary}");
            Check("run D: the plan ends by itself after the last window, without a clock time", !d.TimedOut && d.End.Summary.StartsWith("No NSTN window opens again tonight"), d.End.Summary);
            Check("run D: only H-alpha was still open", d.Captures > 0 && d.CapturesPerFilter.Keys.All(k => k == "Ha"),
                string.Join(", ", d.CapturesPerFilter.Select(kv => $"{kv.Key} {kv.Value}")));
        }

        // -------------------------------------------------------------------
        // 4. Validate
        // -------------------------------------------------------------------

        /// <summary>A bare immutable container, to show what N.I.N.A. does without help.</summary>
        private sealed class BareImmutable : SequentialContainer, IImmutableContainer {
        }

        private static List<string> SequencerValidate(ISequenceContainer container) {
            var sequencer = new global::NINA.Sequencer.Sequencer(new SequenceRootContainer());
            var method = typeof(global::NINA.Sequencer.Sequencer).GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(ISequenceContainer) });
            return ((IList<string>)method.Invoke(sequencer, new object[] { container })).Distinct().ToList();
        }

        private static void ValidateChecks() {
            Console.WriteLine();
            Console.WriteLine("== 4. Validate (Sequencer.Validate before the start, filter wheel not connected) ==");
            var plain = new SequentialContainer { Name = "Plain" };
            plain.Add(new SequentialContainer { Name = "inner" });
            ((SequentialContainer)plain.Items[0]).Add(new SwitchFilter(profile, filterWheel) { Filter = profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters[1] });
            var bare = new SequentialContainer { Name = "Bare" };
            var immutable = new BareImmutable { Name = "immutable" };
            immutable.Add(new SwitchFilter(profile, filterWheel) { Filter = profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters[1] });
            bare.Add(immutable);
            var plainIssues = SequencerValidate(plain);
            var bareIssues = SequencerValidate(bare);
            Check("control: issue of a Switch Filter in a normal container is reported", plainIssues.Count == 1, string.Join(" / ", plainIssues));
            Check("N.I.N.A. skips the children of an immutable container", bareIssues.Count == 0, string.Join(" / ", bareIssues));

            var withPlan = new SequentialContainer { Name = "WithPlan" };
            var plan = (NstnFilterPlan)factory.GetContainer<NstnFilterPlan>();
            plan.Rows.Add(new FilterPlanRow { FilterName = "OIII", Count = 5, ExposureSeconds = 300 });
            withPlan.Add(plan);
            var planIssues = SequencerValidate(withPlan);
            foreach (var issue in planIssues) {
                Console.WriteLine($"     issue: {issue}");
            }
            Check("the plan reports its children's issues (filter wheel)", planIssues.Contains(plainIssues.FirstOrDefault()), "");
            Check("the plan reports a filter missing in the profile", planIssues.Any(i => i.Contains("OIII") && i.Contains("filter list")), "");
            Check("the plan reports issues of its NSTN conditions", planIssues.Any(i => i.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0), "");
            Check("plan.Validate() is false", !plan.Validate(), "");
        }

        // -------------------------------------------------------------------

        private static void SameCopy(string what, NstnFilterPlan source, NstnFilterPlan copy) {
            Check($"{what}: rows equal", Rows(copy) == Rows(source), Rows(copy));
            Check($"{what}: rows are new objects", source.Rows.Zip(copy.Rows).All(p => !ReferenceEquals(p.First, p.Second)), "");
            Check($"{what}: children match the rows", copy.ActualStructure == copy.ExpectedStructure && copy.ActualStructure == source.ActualStructure, copy.ActualStructure);
            var shared = Descendants(copy).Intersect(Descendants(source), ReferenceEqualityComparer.Instance).Count();
            Check($"{what}: no child shared with the source", shared == 0, $"{shared} shared");
            Check($"{what}: parents point into the copy", ParentsConsistent(copy), "");
            Check($"{what}: name kept", copy.Name == source.Name, copy.Name);
            Check($"{what}: plan values equal", copy.Gain == source.Gain && copy.Offset == source.Offset && Equals(copy.Binning, source.Binning) && copy.RoundsPerFilter == source.RoundsPerFilter,
                $"g{copy.Gain} o{copy.Offset} {copy.Binning} r{copy.RoundsPerFilter}");
            Check($"{what}: binning is a new object", !ReferenceEquals(copy.Binning, source.Binning), "");
            Check($"{what}: stop after failures equal", copy.StopAfterFailures == source.StopAfterFailures, $"{copy.StopAfterFailures}");
        }

        private static IEnumerable<object> Descendants(ISequenceContainer container) {
            foreach (var condition in ((IConditionable)container).GetConditionsSnapshot()) {
                yield return condition;
            }
            foreach (var item in container.GetItemsSnapshot()) {
                yield return item;
                if (item is ISequenceContainer child) {
                    foreach (var nested in Descendants(child)) {
                        yield return nested;
                    }
                }
            }
        }

        private static bool ParentsConsistent(ISequenceContainer container) {
            foreach (var condition in ((IConditionable)container).GetConditionsSnapshot()) {
                if (condition.Parent != container) {
                    return false;
                }
            }
            foreach (var item in container.GetItemsSnapshot()) {
                if (item.Parent != container) {
                    return false;
                }
                if (item is ISequenceContainer child && !ParentsConsistent(child)) {
                    return false;
                }
            }
            return true;
        }

        private static TakeExposure FirstExposure(NstnFilterPlan plan) =>
            (TakeExposure)((ISequenceContainer)((ISequenceContainer)plan.Items[1]).Items[1]).Items[0];

        /// <summary>The structure without the plan values of each block.</summary>
        private static string Plain(string structure) => Regex.Replace(structure, @" g-?\d+ o-?\d+ \d+x\d+ r\d+", "");

        private static string Rows(NstnFilterPlan plan) =>
            string.Join(", ", plan.Rows.Select(r => $"{r.FilterName} {r.Count}x{r.ExposureSeconds}"));

        private static string Describe(ISequenceItem item) => item == null ? "null" : $"{item.GetType().Name} '{item.Name}'";

        private static string Sha256(string path) {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).Substring(0, 16).ToLowerInvariant();
        }

        private static void Check(string name, bool ok, string detail) {
            if (ok) {
                passed++;
                Console.WriteLine($"  ok    {name}");
            } else {
                failed++;
                Console.WriteLine($"  FAIL  {name}  [{detail}]");
            }
        }

        private static void Fail(string name, string detail) => Check(name, false, detail);
    }
}
