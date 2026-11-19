using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Plugin.Nstn.FilterPlan;
using NINA.Plugin.Nstn.Instructions;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Trigger.Autofocus;
using NINA.Sequencer.Trigger.Guider;
using NINA.Sequencer.Trigger.MeridianFlip;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Model;
using NINA.WPF.Base.Utility.AutoFocus;
using Nstn.Core;

namespace NINA.Plugin.Nstn.Checks {

    /// <summary>
    /// One run of a filter plan by N.I.N.A.'s own SequentialStrategy, without its window:
    /// a target container (Dec +70°, always up in Kordel; SQM 20.9 and the filter
    /// assignments of the test profile) with the plan and, on the target container,
    /// Meridian Flip, Dither after 1 exposure, AF after filter change and a probe that
    /// records every trigger check before an exposure. Devices are stubs: the camera
    /// exposes for real time, the filter wheel remembers its filter, guider and focuser are
    /// connected, the mount is not (Meridian Flip can only be evaluated). NSTN's clock
    /// runs from a fixed instant of the night 18/19 Nov 2026, so the windows do not
    /// depend on when the checks run.
    /// </summary>
    internal sealed class PlanRun {

        // The night of the checks (Kordel, CET): O III 01:22–05:56, H-alpha 01:08–06:10,
        // L/RGB 05:04–05:26 for the target below.
        public static readonly Coordinates Target = new Coordinates(Angle.ByHours(15.07), Angle.ByDegree(70), Epoch.J2000);

        private const string Assignments =
            "[{\"NinaName\":\"Askar D1\",\"Preset\":\"Astronomik (6 nm)\",\"Line\":\"OIII\",\"BandwidthNm\":6}," +
            "{\"NinaName\":\"Ha\",\"Preset\":\"Astronomik (6 nm)\",\"Line\":\"Ha\",\"BandwidthNm\":6}," +
            "{\"NinaName\":\"L\",\"Preset\":\"\",\"Line\":\"RGB\",\"BandwidthNm\":89}]";

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<string> timeline = new ConcurrentQueue<string>();

        public int Captures { get; private set; }
        public int FilterChanges { get; private set; }
        public int Autofocus { get; private set; }
        public int Dither { get; private set; }
        public int Probes { get; private set; }
        public Dictionary<string, int> CapturesPerFilter { get; } = new Dictionary<string, int>();
        /// <summary>NSTN's clock at the first exposure of each filter.</summary>
        public Dictionary<string, DateTime> FirstCapture { get; } = new Dictionary<string, DateTime>();
        /// <summary>The filters of the exposures in the order they were taken.</summary>
        public List<string> CaptureOrder { get; } = new List<string>();
        /// <summary>Gain, offset and binning the camera was asked for, per filter (all exposures of it).</summary>
        public Dictionary<string, HashSet<string>> CaptureCamera { get; } = new Dictionary<string, HashSet<string>>();
        public NstnFilterPlan Plan { get; private set; }
        public NstnNightLeftCondition End { get; private set; }
        public NightPlan NightPlan { get; private set; }
        public IReadOnlyList<string> WindowsShown { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> Findings { get; private set; } = Array.Empty<string>();
        public bool TimedOut { get; private set; }
        public TimeSpan Duration { get; private set; }
        public IReadOnlyCollection<string> Timeline => timeline;

        private void Note(string what) => timeline.Enqueue($"{clock.Elapsed.TotalSeconds,7:0.0}s  {what}");

        private sealed class ProbeTrigger : SequenceTrigger {

            private readonly PlanRun run;

            public ProbeTrigger(PlanRun run) {
                this.run = run;
                Name = "Probe";
            }

            public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
                if (nextItem is TakeExposure) {
                    run.Probes++;
                }
                return false;
            }

            public override Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) => Task.CompletedTask;

            public override object Clone() => new ProbeTrigger(run);
        }

        /// <summary>Runs the plan from <paramref name="startUtc"/> (NSTN's clock) for at most <paramref name="limit"/>.</summary>
        public static PlanRun Execute(string outDir, DateTime startUtc, TimeSpan limit, int roundsPerFilter, bool imagesMissing, double? narrowbandThreshold = null,
                IReadOnlyList<FilterPlanRow> rows = null) {
            var run = new PlanRun();
            run.Run(outDir, startUtc, limit, roundsPerFilter, imagesMissing, narrowbandThreshold, rows);
            return run;
        }

        private void Run(string outDir, DateTime startUtc, TimeSpan limit, int roundsPerFilter, bool imagesMissing, double? narrowbandThreshold,
                IReadOnlyList<FilterPlanRow> rows) {
            var profile = Stub.Create<IProfileService>();
            var p = profile.ActiveProfile;
            p.FilterWheelSettings.FilterWheelFilters.Add(new FilterInfo("Askar D1", 0, 0));
            p.FilterWheelSettings.FilterWheelFilters.Add(new FilterInfo("Ha", 0, 1));
            p.FilterWheelSettings.FilterWheelFilters.Add(new FilterInfo("L", 0, 2));
            // R, G and B for an L/RGB row; the default rows do not use them.
            p.FilterWheelSettings.FilterWheelFilters.Add(new FilterInfo("R", 0, 3));
            p.FilterWheelSettings.FilterWheelFilters.Add(new FilterInfo("G", 0, 4));
            p.FilterWheelSettings.FilterWheelFilters.Add(new FilterInfo("B", 0, 5));
            p.AstrometrySettings.Latitude = 49.8;
            p.AstrometrySettings.Longitude = 6.6;
            var images = Path.Combine(outDir, "images");
            Directory.CreateDirectory(images);
            p.ImageFileSettings.FilePath = imagesMissing ? Path.Combine(outDir, "no-such-folder") : images;
            // The NSTN options of the test profile (SQM, filter assignments).
            ((Stub)(object)p.PluginSettings).Handler = (method, args) => {
                if (method.Name != "TryGetValue") {
                    return Stub.NotHandled;
                }
                var key = (string)args[1];
                var type = method.GetParameters()[2].ParameterType.GetElementType();
                if (key == "SkyQuality" && type == typeof(double)) {
                    args[2] = 20.9;
                    return true;
                }
                if (key == "Assignments" && type == typeof(string)) {
                    args[2] = Assignments;
                    return true;
                }
                if (key == "ThresholdNarrowband" && type == typeof(double) && narrowbandThreshold is double threshold) {
                    args[2] = threshold;
                    return true;
                }
                return false;
            };

            var cameraInfo = new CameraInfo { Connected = true };
            var wheelInfo = new FilterWheelInfo { Connected = true };
            var guiderInfo = new GuiderInfo { Connected = true };
            var focuserInfo = new FocuserInfo { Connected = true };
            var mountInfo = new TelescopeInfo { Connected = false };

            var camera = Stub.Create<ICameraMediator>((m, a) => m.Name == "GetInfo" ? cameraInfo : Stub.NotHandled);
            var wheel = Stub.Create<IFilterWheelMediator>((m, a) => {
                if (m.Name == "GetInfo") {
                    return wheelInfo;
                }
                if (m.Name == "ChangeFilter") {
                    var filter = (FilterInfo)a[0];
                    wheelInfo.SelectedFilter = filter;
                    FilterChanges++;
                    Note($"FILTER -> {filter?.Name}");
                    return Task.FromResult(filter);
                }
                return Stub.NotHandled;
            });
            var imaging = Stub.Create<IImagingMediator>((m, a) => {
                if (m.Name != "CaptureImage") {
                    return Stub.NotHandled;
                }
                var capture = (CaptureSequence)a[0];
                var token = (CancellationToken)a[1];
                var filter = wheelInfo.SelectedFilter?.Name ?? "-";
                return Task.Run(async () => {
                    lock (CapturesPerFilter) {
                        Captures++;
                        CapturesPerFilter[filter] = CapturesPerFilter.TryGetValue(filter, out var n) ? n + 1 : 1;
                        FirstCapture.TryAdd(filter, NstnClock.UtcNow);
                        CaptureOrder.Add(filter);
                        if (!CaptureCamera.TryGetValue(filter, out var seen)) {
                            CaptureCamera[filter] = seen = new HashSet<string>();
                        }
                        seen.Add($"g{capture.Gain} o{capture.Offset} {capture.Binning?.Name}");
                    }
                    Note($"EXPOSURE {filter} {capture.ExposureTime} s (NSTN clock {NstnClock.UtcNow.ToLocalTime():HH:mm:ss})");
                    await Task.Delay(TimeSpan.FromSeconds(capture.ExposureTime), token);
                    return Stub.Create<IExposureData>();
                });
            });
            var historyList = new List<ImageHistoryPoint>();
            var nextId = 0;
            var history = Stub.Create<IImageHistoryVM>((m, a) => {
                if (m.Name == "get_ImageHistory") {
                    return historyList;
                }
                if (m.Name == "Add" && a.Length == 2) {
                    lock (historyList) {
                        historyList.Add(new ImageHistoryPoint(++nextId, (string)a[1]));
                    }
                    return null;
                }
                return Stub.NotHandled;
            });
            var guider = Stub.Create<IGuiderMediator>((m, a) => {
                if (m.Name == "GetInfo") {
                    return guiderInfo;
                }
                if (m.Name == "Dither") {
                    Dither++;
                    Note("DITHER");
                    return Task.FromResult(true);
                }
                return Stub.NotHandled;
            });
            var focuser = Stub.Create<IFocuserMediator>((m, a) => m.Name == "GetInfo" ? focuserInfo : Stub.NotHandled);
            var autofocusVM = Stub.Create<IAutoFocusVM>((m, a) => {
                if (m.Name == "StartAutoFocus") {
                    Autofocus++;
                    Note($"AUTOFOCUS with {((FilterInfo)a[0])?.Name}");
                    return Task.FromResult(new AutoFocusReport());
                }
                return Stub.NotHandled;
            });
            var autofocusFactory = Stub.Create<IAutoFocusVMFactory>((m, a) => m.Name == "Create" ? autofocusVM : Stub.NotHandled);
            var mount = Stub.Create<ITelescopeMediator>((m, a) => m.Name == "GetInfo" ? mountInfo : Stub.NotHandled);

            object Provide(Type t) {
                if (t == typeof(IProfileService)) return profile;
                if (t == typeof(ICameraMediator)) return camera;
                if (t == typeof(IFilterWheelMediator)) return wheel;
                if (t == typeof(IImagingMediator)) return imaging;
                if (t == typeof(IImageHistoryVM)) return history;
                if (t == typeof(IGuiderMediator)) return guider;
                if (t == typeof(IFocuserMediator)) return focuser;
                if (t == typeof(IAutoFocusVMFactory)) return autofocusFactory;
                if (t == typeof(ITelescopeMediator)) return mount;
                return t.IsInterface ? Stub.Create(t) : null;
            }
            T Construct<T>() {
                var ctor = typeof(T).GetConstructors().First(c => c.GetCustomAttribute<ImportingConstructorAttribute>() != null);
                return (T)ctor.Invoke(ctor.GetParameters().Select(x => Provide(x.ParameterType)).ToArray());
            }

            NstnClock.RunFrom(startUtc);
            try {
                var root = new SequenceRootContainer { Name = "Root" };
                var dso = Construct<DeepSkyObjectContainer>();
                dso.Name = "Check target";
                dso.Target = new InputTarget(Angle.ByDegree(49.8), Angle.ByDegree(6.6), null) {
                    TargetName = "Check target Dec +70",
                    InputCoordinates = new InputCoordinates(Target),
                };
                root.Add(dso);
                dso.Add(new ProbeTrigger(this));
                dso.Add(Construct<MeridianFlipTrigger>());
                var dither = Construct<DitherAfterExposures>();
                dither.AfterExposures = 1;
                dso.Add(dither);
                dso.Add(Construct<AutofocusAfterFilterChange>());

                Plan = Construct<NstnFilterPlan>();
                Plan.Name = "NSTN filter plan";
                Plan.Rows.Clear();
                foreach (var row in rows ?? new[] {
                        new FilterPlanRow { FilterName = "Askar D1", Count = 2, ExposureSeconds = 1 },
                        new FilterPlanRow { FilterName = "Ha", Count = 2, ExposureSeconds = 1 },
                        new FilterPlanRow { FilterName = "L", Count = 2, ExposureSeconds = 1 },
                    }) {
                    Plan.Rows.Add(row);
                }
                Plan.RoundsPerFilter = roundsPerFilter;
                dso.Add(Plan);
                End = (NstnNightLeftCondition)Plan.Conditions[0];

                // Let the conditions compute their nights once.
                Plan.Validate();
                Thread.Sleep(500);
                Plan.Validate();
                // What the plan's view shows before the run: the hidden waiting instruction's night plan.
                NightPlan = NightPlanBuilder.Build(Plan, new NstnOptions(profile), NstnClock.UtcNow);
                // Night Info's sequence check on N.I.N.A.'s real tree (real trigger types, the real plan).
                Findings = SequenceCheck.Check(SequenceTree.Convert(dso),
                    new SequenceCheckContext(false, null, _ => null, ms => NightModel.ToUtcDateTime(ms).ToLocalTime().ToString("HH:mm")))
                    .Select(f => $"{f.Level}: {f.Message}").ToList();
                WindowsShown = Plan.Windows.Select(w => $"{w.Parent?.Name}: {w.WindowSummary} | {w.NowSummary}").ToList();

                Note($"RUN start, NSTN clock {NstnClock.UtcNow.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                using var cts = new CancellationTokenSource(limit);
                var started = Stopwatch.StartNew();
                try {
                    dso.Run(new Progress<ApplicationStatus>(_ => { }), cts.Token).GetAwaiter().GetResult();
                } catch (OperationCanceledException) {
                    // the time limit
                }
                Duration = started.Elapsed;
                TimedOut = cts.IsCancellationRequested;
                Note($"RUN end after {Duration.TotalSeconds:0.0} s: plan {Plan.Status}, timed out {TimedOut}, end condition: {End.Summary}");
            } finally {
                NstnClock.Reset();
            }
        }

        public void WriteTimeline(string path) => File.WriteAllLines(path, timeline);
    }
}
