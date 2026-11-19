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
            if (args.Length > 2 && args[1] == "write-sequence") {
                // A user's setup: SQM and the filters assigned. The blocks then save their real
                // bands, and the sequence check below judges the sample, not a bare profile.
                SetNstnOptions(profile, 21.0,
                    "[{\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}," +
                    "{\"NinaName\":\"R\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}," +
                    "{\"NinaName\":\"G\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}," +
                    "{\"NinaName\":\"B\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}," +
                    "{\"NinaName\":\"Ha\",\"Preset\":\"\",\"Line\":\"Ha\",\"BandwidthNm\":7}]");
                // The English sample at the given path, the German one next to it (.de.json).
                var failed = false;
                foreach (var (path, language) in new[] { (args[2], "en-US"), (Path.ChangeExtension(args[2], ".de.json"), "de-DE") }) {
                    var root = SequenceWriter.WriteShareable(path, profile, converter, template, language);
                    var problems = SequenceWriter.RoundTrip(path, profile, root, template);
                    Console.WriteLine(problems.Count == 0 ? $"{Path.GetFileName(path)}: load and save again identical, no unknown types" : string.Join(Environment.NewLine, problems));
                    // What Night Info's sequence check says about the sample: a warning there is a broken sample.
                    var target = root.Items.OfType<TargetAreaContainer>().SelectMany(t => t.Items).OfType<DeepSkyObjectContainer>().Single();
                    var findings = SequenceCheck.Check(SequenceTree.Convert(target),
                        new SequenceCheckContext(false, null, _ => null, ms => NightModel.ToUtcDateTime(ms).ToLocalTime().ToString("HH:mm")));
                    foreach (var finding in findings) {
                        Console.WriteLine($"  sequence check: {finding.Level}: {finding.Message}");
                    }
                    failed |= problems.Count > 0 || findings.Any(f => f.Level == FindingLevel.Warning);
                }
                Logger.CloseAndFlush();
                return failed ? 1 : 0;
            }
            try {
                CloneChecks();
                DropAndTemplateChecks();
                SaveLoadChecks();
                ValidateChecks();
                FailureChecks();
                GoldenChecks();
                NeverAtSiteChecks();
                PlanFollowsRowsChecks();
                AssignmentChecks();
                OwnWindowChecks();
                SunriseChecks(runs);
                LrgbAndPriorityChecks(runs);
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
        // A band that never opens at the site: the text of the condition
        // -------------------------------------------------------------------

        private static void NeverAtSiteChecks() {
            Console.WriteLine();
            Console.WriteLine("== Never opens at this site (broadband and narrowband text) ==");
            // SQM 20.5 is below the broadband threshold 20.6; at 17.0 H-alpha 7 nm cannot reach 23.1 in band either.
            foreach (var (line, sqm, key) in new[] { (Bands.Broadband, 20.5, "Cond_NeverAtSiteBroadband"), ("Ha", 17.0, "Cond_NeverAtSite") }) {
                var site = SiteProfile(sqm);
                var dso = TargetM33(site);
                var condition = new NstnWindowCondition(site) { Line = line, BandwidthNm = 7 };
                var block = new SequentialContainer { Name = "Block" };
                block.Add(condition);
                dso.Add(block);
                // Adding it schedules a forced refresh in the background; until that has run a
                // refresh without the target may still be cached.
                BandForecast forecast = null;
                for (var i = 0; i < 50 && (forecast = condition.CurrentForecast()) == null; i++) {
                    System.Threading.Thread.Sleep(100);
                }
                if (forecast == null) {
                    Check($"{line}: a forecast", false, string.Join(" / ", condition.Issues));
                    continue;
                }
                var expected = line == Bands.Broadband
                    ? Text.T(key, condition.ShortLabel, forecast.MinimumSkyBrightness, forecast.SqmZenith)
                    : Text.T(key, condition.ShortLabel, condition.BandwidthNm, forecast.MinimumSkyBrightness, forecast.SqmZenith);
                Check($"{line} at SQM {sqm}: never opens at this site", forecast.NeverOpensAtSite, $"needs {forecast.MinimumSkyBrightness:0.00}, site {forecast.SqmZenith:0.00}");
                Check($"{line} at SQM {sqm}: its own text ({key})", condition.WindowSummary == expected, condition.WindowSummary);
            }
        }

        // -------------------------------------------------------------------
        // The night plan of the filter plan follows its rows
        // -------------------------------------------------------------------

        private static void PlanFollowsRowsChecks() {
            Console.WriteLine();
            Console.WriteLine("== Night plan after a row is added (the diagram follows the rows) ==");
            var site = SiteProfile(20.9, new[] { "Ha", "O3", "L" },
                "[{\"NinaName\":\"Ha\",\"Preset\":\"\",\"Line\":\"Ha\",\"BandwidthNm\":7}," +
                "{\"NinaName\":\"O3\",\"Preset\":\"\",\"Line\":\"OIII\",\"BandwidthNm\":7}," +
                "{\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}]");
            var dso = TargetM33(site);
            var info = new NstnNightInfo(site);
            dso.Add(info);
            var plan = new NstnFilterPlan(site, camera, imaging, imageSave, history, filterWheel) { Name = "NSTN filter plan", Category = "NSTN" };
            plan.Rows.Clear();
            plan.Rows.Add(new FilterPlanRow { FilterName = "Ha" });
            dso.Add(plan);
            Check("one row: the night plan shows it", WaitFor(() => PlanLabels(plan) == "Ha: 5 × 300 s"), PlanState(plan));
            // Night Info next to the plan: the target is not in a sequence here, so its 20 s
            // refresh never runs - only a refresh the plan asks for can bring the new bands.
            info.RecalculateCommand.Execute(null);
            Check("Night Info: one band", WaitFor(() => ChartBands(info) == 1), $"{ChartBands(info)} bands");
            // Adding a row rebuilds the children; the new waiting instruction must plan with all of them.
            plan.Rows.Add(new FilterPlanRow { FilterName = "O3" });
            plan.Rows.Add(new FilterPlanRow { FilterName = "L", Count = 10, ExposureSeconds = 120 });
            Check("rows added: the night plan shows all rows without waiting for its 20 s refresh",
                WaitFor(() => PlanLabels(plan) == "Ha: 5 × 300 s | O3: 5 × 300 s | L: 10 × 120 s"), PlanLabels(plan));
            var lines = string.Join(" | ", plan.Windows.Select(w => w.Line));
            Check("each row's condition rates its own band", lines == "Ha | OIII | RGB", lines);
            Check("Night Info next to the plan shows the new bands without waiting for its 20 s refresh",
                WaitFor(() => ChartBands(info) == 3), $"{ChartBands(info)} bands: {string.Join(", ", info.Chart.Rows.Where(r => !r.IsMoon).Select(r => r.Label))}");
        }

        private static string PlanLabels(NstnFilterPlan plan) => string.Join(" | ", plan.Waiter?.Plan.Rows.Select(r => r.Label) ?? Enumerable.Empty<string>());

        private static string PlanState(NstnFilterPlan plan) {
            var waiter = plan.Waiter;
            if (waiter == null) {
                return "no waiter";
            }
            var p = waiter.Plan;
            return $"rows '{PlanLabels(plan)}', HasPlan {p.HasPlan}, summary '{p.Summary}', notes '{p.Notes}', issues '{string.Join(" / ", waiter.Issues)}', wait '{waiter.WaitSummary}', parent {waiter.Parent?.Name}";
        }

        private static int ChartBands(NstnNightInfo info) => info.Chart?.Rows?.Count(r => !r.IsMoon) ?? 0;

        /// <summary>
        /// Up to 15 s: without N.I.N.A.'s database every UT1-UTC lookup fails slowly here.
        /// The 20 s refreshes do not run (the target is not in a sequence), so waiting
        /// longer cannot make a missing refresh look present.
        /// </summary>
        private static bool WaitFor(Func<bool> done) {
            for (var i = 0; i < 150; i++) {
                if (done()) {
                    return true;
                }
                System.Threading.Thread.Sleep(100);
            }
            return done();
        }

        // -------------------------------------------------------------------
        // Filters of this profile: a set preset and the line in the filter's name
        // -------------------------------------------------------------------

        private static void AssignmentChecks() {
            Console.WriteLine();
            Console.WriteLine("== Filters of this profile: a set preset takes the line the filter's name names ==");
            const string set = "Astronomik (6 nm)";
            foreach (var (name, expected) in new[] { ("S2", "SII"), ("O3", "OIII"), ("Ha", "Ha"), ("SII 6nm", "SII") }) {
                var row = new FilterAssignmentRow(name, null, () => { }) { Choice = set };
                Check($"'{name}' + {set}: line {expected}", row.Line == expected, row.Line);
            }
            var wrong = new FilterAssignmentRow("S2", new FilterAssignment("S2", set, "Ha", 6), () => { });
            var remark = Text.T("Assign_NameMismatch", Bands.ShortLabel("SII"), Bands.ShortLabel("Ha"));
            Check("'S2' saved as H-alpha: the result says the name names S II", wrong.Result.EndsWith(remark, StringComparison.Ordinal), wrong.Result);
            var right = new FilterAssignmentRow("S2", new FilterAssignment("S2", set, "SII", 6), () => { });
            Check("'S2' saved as S II: no remark", right.Result == "→ " + right.ToAssignment().Describe(), right.Result);
        }

        // -------------------------------------------------------------------
        // A row's own window: priority, threshold, altitude, time span
        // -------------------------------------------------------------------

        private static void OwnWindowChecks() {
            Console.WriteLine();
            Console.WriteLine("== A row's own window (priority, threshold, altitude, time span) ==");
            var site = SiteProfile(21.3, new[] { "Ha", "O3", "L" },
                "[{\"NinaName\":\"Ha\",\"Preset\":\"\",\"Line\":\"Ha\",\"BandwidthNm\":7}," +
                "{\"NinaName\":\"O3\",\"Preset\":\"\",\"Line\":\"OIII\",\"BandwidthNm\":7}," +
                "{\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}]");
            var dso = TargetM33(site);
            var plan = new NstnFilterPlan(site, camera, imaging, imageSave, history, filterWheel) { Name = "NSTN filter plan", Category = "NSTN" };
            plan.Rows.Clear();
            plan.Rows.Add(new FilterPlanRow { FilterName = "Ha" });
            plan.Rows.Add(new FilterPlanRow { FilterName = "O3", Priority = true, ThresholdOffset = 0.3, NotBefore = "22:00", NotAfter = "2:00" });
            plan.Rows.Add(new FilterPlanRow { FilterName = "L", Count = 10, ExposureSeconds = 120, MinAltitude = 40 });
            dso.Add(plan);

            Check("priority row runs first and keeps its window (no iteration limit)",
                Plain(plan.ActualStructure) == "Wait | O3 5x300 g-1 o-1 1x1 r- w+0.3/-/22:00-02:00 | Ha 5x300 | L 10x120 w0/40/--- | End", plan.ActualStructure);
            Check("own window: children match the rows", plan.ActualStructure == plan.ExpectedStructure, plan.ExpectedStructure);
            var o3 = plan.Windows[0];
            Check("own window: the block's condition carries the row's limits",
                o3.ThresholdOffset == 0.3 && o3.NotBefore == "22:00" && o3.NotAfter == "02:00" && o3.MinAltitude == null && plan.Windows[2].MinAltitude == 40,
                $"{o3.ThresholdOffset} {o3.NotBefore}-{o3.NotAfter} {o3.MinAltitude} / L {plan.Windows[2].MinAltitude}");
            Check("outline names the priority and the limits", plan.Outline.Contains(Text.T("Plan_OutlineRowPriority", 2, "O3: 5 × 300 s"), StringComparison.Ordinal)
                && plan.Outline.Contains(plan.Rows[1].LimitsText, StringComparison.Ordinal), plan.Outline);
            // The night plan's advice "add Loop For Iterations" is for blocks built by hand, not for a priority row.
            WaitFor(() => plan.Waiter?.Plan.HasPlan == true);
            var advice = Text.T("Plan_Priority", Text.Quote("O3: 5 × 300 s"));
            Check("night plan: no advice to add an iteration limit to a priority row",
                plan.Waiter?.Plan.HasPlan == true && !plan.Waiter.Plan.Notes.Contains(advice, StringComparison.Ordinal), plan.Waiter?.Plan.Notes ?? "no plan");
            Check("row labels: the priority label is its own key", Text.T("Plan_PriorityLabel").Length < 20, Text.T("Plan_PriorityLabel"));

            // Saved with the sequence and read back.
            var root = new SequentialContainer { Name = "Root" };
            root.Add(plan.Clone() as NstnFilterPlan);
            var json = converter.Serialize(root);
            var loaded = ((ISequenceContainer)converter.Deserialize(json)).Items.OfType<NstnFilterPlan>().Single();
            var row = loaded.Rows[1];
            Check("saved and loaded: the row's own window", row.Priority && row.ThresholdOffset == 0.3 && row.NotBefore == "22:00" && row.NotAfter == "02:00"
                && loaded.Rows[2].MinAltitude == 40 && loaded.Rows[0].Limits.IsNone && !loaded.Rows[0].Priority, $"{row.WindowText} / {loaded.Rows[2].WindowText}");
            Check("saved and loaded: children match the rows", loaded.ActualStructure == loaded.ExpectedStructure, loaded.ActualStructure);
            Check("a row without its own window writes none of its fields",
                !Regex.IsMatch(RowJson(json, "Ha"), "Priority|ThresholdOffset|MinAltitude|NotBefore|NotAfter"), RowJson(json, "Ha"));

            // The window itself: the time span and the stricter threshold only narrow it.
            var limited = WaitForForecast(plan.Windows[0]);
            var free = new NstnWindowCondition(site) { Line = "OIII", BandwidthNm = 7 };
            var block = new SequentialContainer { Name = "free" };
            block.Add(free);
            dso.Add(block);
            var plain = WaitForForecast(free);
            var start = WindowLimits.NightMoment(plain?.NightDate ?? default, new TimeOnly(22, 0), TimeZoneInfo.Local);
            var end = WindowLimits.NightMoment(plain?.NightDate ?? default, new TimeOnly(2, 0), TimeZoneInfo.Local);
            Check("own window: a forecast with and without limits", limited != null && plain != null, $"{limited != null} {plain != null}");
            if (limited != null && plain != null) {
                Check("own window: inside 22:00-02:00 and never longer than without limits",
                    limited.Segments.All(s => s.Start >= start && s.End <= end) && limited.OpenMinutes <= plain.OpenMinutes,
                    $"{limited.OpenMinutes:0} min with, {plain.OpenMinutes:0} min without");
            }

            // Opening the settings is no change: no rebuild, the sequence is not marked as changed.
            var waiter = plan.Waiter;
            plan.Rows[0].IsExpanded = true;
            Check("opening a row's own window does not rebuild the plan", ReferenceEquals(waiter, plan.Waiter), "rebuilt");
            plan.Rows[0].MinAltitude = 35;
            Check("changing it does", !ReferenceEquals(waiter, plan.Waiter) && plan.ActualStructure.Contains("w0/35/---", StringComparison.Ordinal), plan.ActualStructure);
            plan.Rows[0].NotBefore = "nachts";
            Check("an unreadable time keeps the old value", plan.Rows[0].NotBefore == null, plan.Rows[0].NotBefore ?? "null");
        }

        private static BandForecast WaitForForecast(NstnWindowCondition condition) {
            BandForecast forecast = null;
            WaitFor(() => (forecast = condition.CurrentForecast()) != null);
            return forecast;
        }

        /// <summary>The JSON object of the row with that filter name.</summary>
        private static string RowJson(string json, string filter) =>
            Regex.Match(json, "\\{[^{}]*\"FilterName\": \"" + Regex.Escape(filter) + "\"[^{}]*\\}").Value;

        // -------------------------------------------------------------------
        // L/RGB as one row in a ratio, and priority that takes over at once
        // -------------------------------------------------------------------

        private const string LrgbAssignments =
            "[{\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}," +
            "{\"NinaName\":\"R\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}," +
            "{\"NinaName\":\"G\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}," +
            "{\"NinaName\":\"B\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}," +
            "{\"NinaName\":\"Ha\",\"Preset\":\"\",\"Line\":\"Ha\",\"BandwidthNm\":7}]";

        private static FilterPlanRow LrgbRow(string ratio, double l, double rgb, bool priority = false) =>
            new FilterPlanRow { ExposureSeconds = l, RgbExposureSeconds = rgb, Ratio = ratio, FilterName = FilterPlanRow.Lrgb, Priority = priority };

        private static void LrgbAndPriorityChecks(bool runs) {
            Console.WriteLine();
            Console.WriteLine("== L/RGB as one row in a ratio; priority takes over after the running exposure ==");
            var site = SiteProfile(21.3, new[] { "L", "R", "G", "B", "Ha", "L-eXtreme" }, LrgbAssignments);
            var dso = TargetM33(site);
            var plan = new NstnFilterPlan(site, camera, imaging, imageSave, history, filterWheel) { Name = "NSTN filter plan", Category = "NSTN" };
            plan.Rows.Clear();
            plan.Rows.Add(new FilterPlanRow { FilterName = "Ha" });
            plan.Rows.Add(LrgbRow("3:1:1:1", 60, 120, priority: true));
            dso.Add(plan);

            Check("the row list offers L/RGB first", plan.RowChoices.FirstOrDefault() == FilterPlanRow.Lrgb, string.Join(", ", plan.RowChoices));
            Check("one block for the set, in the ratio, priority first",
                Plain(plan.ActualStructure) == "Wait | LRGB L 3x60 R 1x120 G 1x120 B 1x120 g-1 o-1 1x1 r- | Ha 5x300 | End", plan.ActualStructure);
            Check("L/RGB: children match the rows", plan.ActualStructure == plan.ExpectedStructure, plan.ExpectedStructure);
            Check("L/RGB: the set's label", plan.Rows[1].Label == "L/RGB: 3:1:1:1 (L 60 s, RGB 120 s)", plan.Rows[1].Label);
            Check("L/RGB: judged by the broadband window", plan.Windows[0].Line == Bands.Broadband && !plan.Windows[0].FollowsBlockFilter, plan.Windows[0].Line);
            plan.Validate();
            // N.I.N.A.'s own items report the stub devices (no camera, no image folder); only the plan's say something here.
            Check("L/RGB: no issue of the plan with L, R, G, B in the profile (L-eXtreme is not taken for L)",
                !plan.Issues.Any(i => i.Contains(FilterPlanRow.Lrgb, StringComparison.Ordinal) || i == Text.T("Plan_IssueMismatch")), string.Join(" / ", plan.Issues));

            plan.Rows[1].Ratio = "0:1:1:1";
            Check("ratio 0:1:1:1 leaves L out", Plain(plan.ActualStructure).StartsWith("Wait | LRGB R 1x120 G 1x120 B 1x120 ", StringComparison.Ordinal), plan.ActualStructure);
            Check("... and the label names only the RGB time", plan.Rows[1].Label == "L/RGB: 0:1:1:1 (RGB 120 s)", plan.Rows[1].Label);
            plan.Rows[1].Ratio = "3:1:1";
            Check("an unreadable ratio keeps the old one", plan.Rows[1].Ratio == "0:1:1:1", plan.Rows[1].Ratio);
            plan.Rows[1].Ratio = "3 / 1 / 1 / 1";
            Check("'3 / 1 / 1 / 1' is read as 3:1:1:1", plan.Rows[1].Ratio == "3:1:1:1", plan.Rows[1].Ratio);

            // Saved with the sequence and read back.
            var root = new SequentialContainer { Name = "Root" };
            root.Add(plan.Clone() as NstnFilterPlan);
            var json = converter.Serialize(root);
            var loaded = ((ISequenceContainer)converter.Deserialize(json)).Items.OfType<NstnFilterPlan>().Single();
            var row = loaded.Rows.Single(r => r.IsLrgb);
            Check("saved and loaded: the set's ratio and both times", row.Ratio == "3:1:1:1" && row.ExposureSeconds == 60 && row.RgbExposureSeconds == 120 && row.Priority,
                RowJson(json, FilterPlanRow.Lrgb));
            Check("saved and loaded: children match the rows", loaded.ActualStructure == loaded.ExpectedStructure, loaded.ActualStructure);
            Check("a single-filter row writes nothing of a set", !Regex.IsMatch(RowJson(json, "Ha"), "Ratio|RgbExposureSeconds"), RowJson(json, "Ha"));
            var back = LrgbRow("3:1:1:1", 60, 120);
            back.FilterName = "Ha";
            Check("a row switched back to one filter forgets the set", back.Ratio == null && back.RgbExposureSeconds == 0 && back.IsSingleFilter, $"{back.Ratio} {back.RgbExposureSeconds}");

            // A filter the options assign to a narrowband line is never taken for L/RGB.
            var narrowL = SiteProfile(21.3, new[] { "L", "R", "G", "B" }, LrgbAssignments.Replace("\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89", "\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"Ha\",\"BandwidthNm\":7"));
            var other = new NstnFilterPlan(narrowL, camera, imaging, imageSave, history, filterWheel) { Name = "NSTN filter plan", Category = "NSTN" };
            other.Rows.Clear();
            other.Rows.Add(LrgbRow("1:1:1:1", 60, 120));
            TargetM33(narrowL).Add(other);
            other.Validate();
            Check("an L assigned to a narrowband line is not taken: the plan says which name it looks for",
                other.Issues.Any(i => i.Contains("L/RGB", StringComparison.Ordinal) && i.Contains("Lum", StringComparison.Ordinal)), string.Join(" / ", other.Issues));

            if (!runs) {
                return;
            }

            // Run F: O III (Askar D1) runs a long set; H-alpha has priority from 05:41. It must
            // take over after the running exposure, not when the 60 exposures are done.
            var f = PlanRun.Execute(Path.Combine(outDir, "run-f"), Night(5, 40, 35), TimeSpan.FromSeconds(50), roundsPerFilter: 1, imagesMissing: false, rows: new[] {
                new FilterPlanRow { FilterName = "Askar D1", Count = 60, ExposureSeconds = 1 },
                new FilterPlanRow { FilterName = "Ha", Count = 5, ExposureSeconds = 1, Priority = true, NotBefore = "05:41" },
            });
            f.WriteTimeline(Path.Combine(outDir, "run-f.txt"));
            var takeover = f.FirstCapture.TryGetValue("Ha", out var first) ? first.ToLocalTime() : (DateTime?)null;
            Console.WriteLine($"  run F (priority from 05:41): {f.Captures} exposures, H-alpha first at {takeover:HH:mm:ss}, order {string.Join(" ", f.CaptureOrder.Take(40))}");
            Check("run F: O III exposed before the priority window opened", f.CapturesPerFilter.ContainsKey("Askar D1"), string.Join(", ", f.CapturesPerFilter.Select(kv => $"{kv.Key} {kv.Value}")));
            Check("run F: H-alpha took over within seconds of 05:41, after the running exposure",
                // Without the takeover H-alpha would wait for the 60 exposures (after 05:41:35, past the run's end).
                takeover is DateTime t && t.TimeOfDay >= new TimeSpan(5, 41, 0) && t.TimeOfDay <= new TimeSpan(5, 41, 6), $"{takeover:HH:mm:ss}");
            var afterTakeover = f.CaptureOrder.SkipWhile(c => c != "Ha").ToList();
            Check("run F: while it is open nothing else runs", afterTakeover.Count > 0 && afterTakeover.All(c => c == "Ha"), string.Join(" ", afterTakeover));

            // Run G: the L/RGB set with priority in its window 05:04-05:26: L, R, G, B in the
            // ratio 3:1:1:1, one after the other, and H-alpha (open too) waits.
            var g = PlanRun.Execute(Path.Combine(outDir, "run-g"), Night(5, 10, 0), TimeSpan.FromSeconds(20), roundsPerFilter: 1, imagesMissing: false, rows: new[] {
                new FilterPlanRow { FilterName = "Ha", Count = 2, ExposureSeconds = 1 },
                LrgbRow("3:1:1:1", 1, 1, priority: true),
            });
            g.WriteTimeline(Path.Combine(outDir, "run-g.txt"));
            Console.WriteLine($"  run G (L/RGB 3:1:1:1 with priority): {g.Captures} exposures, order {string.Join(" ", g.CaptureOrder.Take(24))}");
            Check("run G: L, R, G, B in the ratio 3:1:1:1, in this order", g.CaptureOrder.Count >= 6 && string.Join("", g.CaptureOrder.Take(6)) == "LLLRGB"
                && (g.CaptureOrder.Count < 12 || string.Join("", g.CaptureOrder.Skip(6).Take(6)) == "LLLRGB"), string.Join(" ", g.CaptureOrder));
            Check("run G: H-alpha waits while the L/RGB window is open", !g.CapturesPerFilter.ContainsKey("Ha"), string.Join(", ", g.CapturesPerFilter.Select(kv => $"{kv.Key} {kv.Value}")));
        }

        // -------------------------------------------------------------------
        // Sunrise: the coming night is shown, a run keeps the night it started in
        // -------------------------------------------------------------------

        private static void SunriseChecks(bool runs) {
            Console.WriteLine();
            Console.WriteLine("== Sunrise: the coming night is shown, a run keeps the night it started in ==");
            var night = new DateOnly(2026, 11, 18);
            var sunriseMs = WindowForecaster.Shared.SunriseEnding(night, TimeZoneInfo.Local, 49.8, 6.6);
            Check("Kordel has a sunrise ending the night 18/19 Nov", sunriseMs != null, "none");
            if (sunriseMs == null) {
                return;
            }
            var sunrise = NightModel.ToUtcDateTime(sunriseMs.Value);
            Console.WriteLine($"  sunrise on the grid: {sunrise.ToLocalTime():HH:mm:ss}");

            // 08:58 on the 19th, the moment of Marc's screenshot: "tonight (18 Nov)" planned 0 s.
            // The clock is set first: a forecast computed earlier would be reused for 2 s.
            NstnClock.RunFrom(Night(8, 58, 0));
            try {
                var site = SiteProfile(21.3, new[] { "Ha", "L" },
                    "[{\"NinaName\":\"Ha\",\"Preset\":\"\",\"Line\":\"Ha\",\"BandwidthNm\":7}," +
                    "{\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}]");
                var dso = TargetM33(site);
                var plan = new NstnFilterPlan(site, camera, imaging, imageSave, history, filterWheel) { Name = "NSTN filter plan", Category = "NSTN" };
                plan.Rows.Clear();
                plan.Rows.Add(new FilterPlanRow { FilterName = "Ha" });
                plan.Rows.Add(new FilterPlanRow { FilterName = "L", Count = 10, ExposureSeconds = 120 });
                dso.Add(plan);

                var forecast = WaitForForecast(plan.Windows[0]);
                Check("08:58, sun up: the windows are those of the coming night 19/20 Nov",
                    forecast?.NightDate == night.AddDays(1), forecast?.NightDate.ToString() ?? "no forecast");
                var title = Text.T("Plan_SectionNight", Text.Night(night.AddDays(1)));
                Check("08:58: the plan's heading names the coming night",
                    WaitFor(() => plan.Waiter?.NightTitle == title && plan.Waiter.Plan.HasPlan), $"'{plan.Waiter?.NightTitle}', want '{title}'");
                var summary = plan.Waiter?.Plan.Summary ?? string.Empty;
                Check("08:58: the night plan plans the coming night instead of 0 s",
                    summary != Text.T("Plan_NothingLeft") && plan.Waiter.Plan.Rows.Any(r => r.Expected != Text.T("Plan_Nothing")), summary);
                var opens = plan.Windows.Select(w => w.CurrentForecast()).Where(f => f != null && f.Segments.Count > 0).Select(f => f.Segments[0].Start).DefaultIfEmpty(0).Min();
                var opensClock = Text.Clock(NightModel.ToUtcDateTime(opens).ToLocalTime());
                Check($"08:58: planned from the first window ({opensClock}), not from now",
                    opens > 0 && summary.Contains(opensClock, StringComparison.Ordinal) && !summary.Contains("08:58", StringComparison.Ordinal), summary);
                Check("08:58: the row says when its window opens, without a sky value of daylight",
                    plan.Windows.All(w => w.NowSummary.StartsWith(Text.T("Cond_ClosedOpens", "").TrimEnd(), StringComparison.Ordinal) && !w.NowSummary.Contains("mag")),
                    string.Join(" / ", plan.Windows.Select(w => w.NowSummary)));

                // 14:00: inside the coming night's 24 hours, the sun still up.
                NstnClock.RunFrom(Night(14, 0, 0));
                WaitFor(() => plan.Windows[0].CurrentForecast() != null && plan.Windows[0].NowSummary.Contains(Text.T("Cond_SunUp", "").Trim(), StringComparison.Ordinal));
                Check("14:00: 'sun above the horizon' instead of a sky value", plan.Windows[0].NowSummary.Contains(Text.T("Cond_SunUp", "").Trim(), StringComparison.Ordinal)
                    && !plan.Windows[0].NowSummary.Contains("mag"), plan.Windows[0].NowSummary);
            } finally {
                NstnClock.Reset();
            }

            if (!runs) {
                return;
            }

            // A plan still exposing at sunrise (Marc's narrowband threshold 2.4 keeps H-alpha
            // open into the day) ends when its night is over instead of waiting for the evening.
            var e = PlanRun.Execute(Path.Combine(outDir, "run-e"), sunrise.AddSeconds(-20), TimeSpan.FromSeconds(60), roundsPerFilter: 1, imagesMissing: false, narrowbandThreshold: 2.4);
            e.WriteTimeline(Path.Combine(outDir, "run-e.txt"));
            Console.WriteLine($"  run E (across sunrise): {e.Captures} exposures, ended after {e.Duration.TotalSeconds:0.0} s: {e.End.Summary}");
            Check("run E: exposures before sunrise", e.Captures > 0, $"{e.Captures}");
            Check("run E: at sunrise the plan ends, its night is over (no wait for the evening)",
                !e.TimedOut && e.End.Summary == Text.T("NightLeft_NightOver", Text.Night(night)), $"timed out {e.TimedOut}: {e.End.Summary}");

            // The waiting instruction alone: begun just before sunrise with nothing left in its
            // night, it gives up at its first look after sunrise (every minute).
            NstnClock.RunFrom(sunrise.AddSeconds(-5));
            try {
                var site = SiteProfile(21.3, new[] { "Ha" }, "[{\"NinaName\":\"Ha\",\"Preset\":\"\",\"Line\":\"Ha\",\"BandwidthNm\":7}]");
                var dso = TargetM33(site);
                var plan = new NstnFilterPlan(site, camera, imaging, imageSave, history, filterWheel) { Name = "NSTN filter plan", Category = "NSTN" };
                plan.Rows.Clear();
                plan.Rows.Add(new FilterPlanRow { FilterName = "Ha" });
                dso.Add(plan);
                WaitForForecast(plan.Windows[0]);
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(100));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var cancelled = false;
                try {
                    plan.Waiter.Execute(new Progress<NINA.Core.Model.ApplicationStatus>(_ => { }), cts.Token).GetAwaiter().GetResult();
                } catch (OperationCanceledException) {
                    cancelled = true;
                }
                Console.WriteLine($"  wait across sunrise: returned after {watch.Elapsed.TotalSeconds:0.0} s, cancelled {cancelled}");
                Check("wait begun before sunrise ends after sunrise instead of waiting for the evening", !cancelled, $"{watch.Elapsed.TotalSeconds:0} s");
            } finally {
                NstnClock.Reset();
            }
        }

        // -------------------------------------------------------------------
        // a site for the checks above
        // -------------------------------------------------------------------

        /// <summary>A profile in Kordel with the given SQM, filter wheel and NSTN filter assignments.</summary>
        private static IProfileService SiteProfile(double sqm, string[] filters = null, string assignments = null) {
            var site = Stub.Create<IProfileService>();
            var p = site.ActiveProfile;
            p.AstrometrySettings.Latitude = 49.8;
            p.AstrometrySettings.Longitude = 6.6;
            foreach (var (filter, position) in (filters ?? Array.Empty<string>()).Select((f, i) => (f, i))) {
                p.FilterWheelSettings.FilterWheelFilters.Add(new FilterInfo(filter, 0, (short)position));
            }
            SetNstnOptions(site, sqm, assignments);
            return site;
        }

        /// <summary>The NSTN options of a stub profile: SQM and the filter assignments.</summary>
        private static void SetNstnOptions(IProfileService site, double sqm, string assignments) {
            ((Stub)(object)site.ActiveProfile.PluginSettings).Handler = (method, a) => {
                if (method.Name != "TryGetValue") {
                    return Stub.NotHandled;
                }
                var key = (string)a[1];
                var type = method.GetParameters()[2].ParameterType.GetElementType();
                if (key == "SkyQuality" && type == typeof(double)) {
                    a[2] = sqm;
                    return true;
                }
                if (key == "Assignments" && type == typeof(string) && assignments != null) {
                    a[2] = assignments;
                    return true;
                }
                return false;
            };
        }

        private static DeepSkyObjectContainer TargetM33(IProfileService site) {
            var dso = SequenceWriter.Construct<DeepSkyObjectContainer>(site);
            dso.Target = new NINA.Astrometry.InputTarget(NINA.Astrometry.Angle.ByDegree(49.8), NINA.Astrometry.Angle.ByDegree(6.6), null) {
                TargetName = "M 33",
                InputCoordinates = new NINA.Astrometry.InputCoordinates(new NINA.Astrometry.Coordinates(
                    NINA.Astrometry.Angle.ByHours(1.5639), NINA.Astrometry.Angle.ByDegree(30.66), NINA.Astrometry.Epoch.J2000)),
            };
            return dso;
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
            Check("view: the night plan ends when no window opens again", a.NightPlan.Summary.EndsWith(Text.T("Plan_StopNothingOpens"), StringComparison.Ordinal), a.NightPlan.Summary);
            Check("view: H-alpha gets time until 06:10, L/RGB none", a.NightPlan.Rows[1].Expected != a.NightPlan.Rows[2].Expected && a.NightPlan.Rows[2].Expected == Text.T("Plan_Nothing"),
                string.Join(" / ", a.NightPlan.Rows.Select(r => r.Expected)));
            Check("view: one window line per row, with the night by both dates and the state now",
                a.WindowsShown.Count == 3 && a.WindowsShown.All(w => w.Contains(Text.Night(new DateOnly(2026, 11, 18)), StringComparison.Ordinal) && (w.Contains("Open") || w.Contains("Closed"))),
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
            Check("run D: the plan ends by itself after the last window, without a clock time", !d.TimedOut && d.End.Summary == Text.T("NightLeft_Done"), d.End.Summary);
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
