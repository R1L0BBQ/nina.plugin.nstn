using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using NINA.Core.Locale;
using NINA.Profile.Interfaces;
using NINA.Sequencer;
using Nstn.Core;

namespace NINA.Plugin.Nstn {

    /// <summary>
    /// Binding source of the XAML templates: <c>{nstn:T Key}</c> shows Text.T(Key) and
    /// follows a language change, like N.I.N.A.'s own <c>{ns:Loc Key}</c>.
    /// </summary>
    public sealed class UiText : INotifyPropertyChanged {

        public static UiText Instance { get; } = new UiText();

        private UiText() {
            Text.Changed += (_, _) => Notify();
        }

        public string this[string key] => Text.T(key);

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify() {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) {
                dispatcher.BeginInvoke(new Action(Notify));
                return;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        }
    }

    /// <summary>An NSTN entity whose shown texts are built in code and must be rebuilt in a new language.</summary>
    internal interface IFollowsLanguage {
        void LanguageChanged();
    }

    /// <summary><c>{nstn:T Key}</c> in XAML.</summary>
    public class TExtension : Binding {

        public TExtension(string key) : base($"[{key}]") {
            Mode = BindingMode.OneWay;
            Source = UiText.Instance;
        }
    }

    /// <summary>
    /// Keeps the texts in N.I.N.A.'s language: the profile's language, switched with
    /// the profile or in the options, and N.I.N.A.'s own names of its instructions.
    ///
    /// The names in the instruction list are set by N.I.N.A.'s plugin loader from
    /// the English export metadata after the instances are created, and only once.
    /// Every NSTN entity therefore registers here and gets its name in the user's
    /// language when the plugin initializes (after the loader) and on every change.
    /// </summary>
    internal static class PluginLanguage {

        // Token of a {nina:...} text -> N.I.N.A.'s label key (NINA.Core/Locale/Locale.resx).
        private static readonly IReadOnlyDictionary<string, string> NinaLabels = new Dictionary<string, string> {
            ["LoopForIterations"] = "Lbl_SequenceCondition_LoopCondition_Name",
            ["LoopUntilTime"] = "Lbl_SequenceCondition_TimeCondition_Name",
            ["SmartExposure"] = "Lbl_SequenceItem_Imaging_SmartExposure_Name",
            ["TakeExposure"] = "Lbl_SequenceItem_Imaging_TakeExposure_Name",
            ["SwitchFilter"] = "Lbl_SequenceItem_FilterWheel_SwitchFilter_Name",
            ["MeridianFlip"] = "Lbl_SequenceTrigger_MeridianFlipTrigger_Name",
            ["AutofocusAfterFilterChange"] = "Lbl_SequenceTrigger_AutofocusAfterFilterChangeTrigger_Name",
            ["DitherAfterExposures"] = "Lbl_SequenceTrigger_Guider_DitherAfterExposures_Name",
            ["SequentialSet"] = "Lbl_SequenceContainer_SequentialContainer_Name",
            ["TargetSet"] = "Lbl_SequenceContainer_DeepSkyObjectContainer_Name",
            ["SlewAndCenter"] = "Lbl_SequenceItem_Platesolving_Center_Name",
            ["StartGuiding"] = "Lbl_SequenceItem_Guider_StartGuiding_Name",
            ["Options"] = "LblOptions",
            ["General"] = "LblGeneral",
            ["Astrometry"] = "LblAstrometry",
            ["Equipment"] = "LblEquipment",
            ["FilterWheel"] = "LblFilterWheel",
            ["Plugins"] = "LblPlugins",
        };

        private sealed record Entity(WeakReference<ISequenceEntity> Reference, Func<string> Name, Func<string> Description);

        private static readonly object Gate = new object();
        private static readonly List<Entity> Entities = new List<Entity>();
        private static IProfileService followed;

        /// <summary>The loader appends the plugin's name to every description in the list; keep it.</summary>
        private static readonly string PluginTitle =
            typeof(PluginLanguage).Assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "NSTN";

        public static void Follow(IProfileService profileService) {
            lock (Gate) {
                if (followed != null || profileService == null) {
                    return;
                }
                followed = profileService;
            }
            Text.NinaName = NinaName;
            profileService.LocaleChanged += (_, _) => Apply();
            profileService.ProfileChanged += (_, _) => Apply();
            Apply();
        }

        /// <summary>Gives an NSTN instruction or condition its name and description in the user's language, now and after every change.</summary>
        public static void Register(ISequenceEntity entity, Func<string> name, Func<string> description) {
            lock (Gate) {
                Entities.RemoveAll(e => !e.Reference.TryGetTarget(out _));
                Entities.Add(new Entity(new WeakReference<ISequenceEntity>(entity), name, description));
            }
        }

        /// <summary>Names and descriptions of all live NSTN entities in the current language.</summary>
        public static void Retitle() {
            List<Entity> entities;
            lock (Gate) {
                entities = Entities.ToList();
            }
            foreach (var entity in entities) {
                if (entity.Reference.TryGetTarget(out var target)) {
                    target.Name = entity.Name();
                    target.Description = $"{entity.Description()}{Environment.NewLine}({PluginTitle})";
                    (target as IFollowsLanguage)?.LanguageChanged();
                }
            }
        }

        private static void Apply() {
            var language = followed?.ActiveProfile?.ApplicationSettings?.Language;
            if (language != null) {
                Text.Culture = language;
            }
            Retitle();
        }

        private static string NinaName(string token) {
            if (!NinaLabels.TryGetValue(token, out var key)) {
                return null;
            }
            var label = Loc.Instance[key];
            return string.IsNullOrEmpty(label) || label.StartsWith("MISSING LABEL", StringComparison.Ordinal) ? null : label;
        }
    }
}
