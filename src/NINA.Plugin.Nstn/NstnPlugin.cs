using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.Core.Model.Equipment;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using Nstn.Core;

namespace NINA.Plugin.Nstn {

    /// <summary>
    /// The plugin manifest. NINA also uses it as the data context of the options
    /// page (Options.xaml), so the settings are exposed here for binding.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class NstnPlugin : PluginBase, INotifyPropertyChanged {

        private readonly IProfileService profileService;
        private readonly NstnOptions options;
        private IReadOnlyList<FilterAssignmentRow> filterRows;
        private INotifyCollectionChanged watchedFilters;

        [ImportingConstructor]
        public NstnPlugin(IProfileService profileService) {
            this.profileService = profileService;
            options = new NstnOptions(profileService);
            profileService.ProfileChanged += OnProfileChanged;
            PluginLanguage.Follow(profileService);
            TakeSuggestionsCommand = new ActionCommand(TakeSuggestions);
            ReloadFiltersCommand = new ActionCommand(BuildRows);
            WatchFilterList();
        }

        /// <summary>
        /// Runs after N.I.N.A.'s loader has given the instructions their English names
        /// from the export metadata: now they get the names in the user's language.
        /// </summary>
        public override Task Initialize() {
            PluginLanguage.Retitle();
            return base.Initialize();
        }

        public override Task Teardown() {
            profileService.ProfileChanged -= OnProfileChanged;
            if (watchedFilters != null) {
                watchedFilters.CollectionChanged -= OnFilterListChanged;
            }
            return base.Teardown();
        }

        // -------------------------------------------------------------------
        // filter assignment: one row per filter of the profile's filter wheel
        // -------------------------------------------------------------------

        public IReadOnlyList<FilterAssignmentRow> FilterRows {
            get {
                if (filterRows == null) {
                    BuildRows();
                }
                return filterRows;
            }
        }

        public bool HasProfileFilters => FilterRows.Count > 0;

        public ICommand TakeSuggestionsCommand { get; }

        public ICommand ReloadFiltersCommand { get; }

        private void BuildRows() {
            var saved = options.Assignments;
            var names = (profileService.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters ?? Enumerable.Empty<FilterInfo>())
                .Select(f => f?.Name?.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            filterRows = names
                .Select(n => new FilterAssignmentRow(n, FilterAssignments.Find(saved, n), SaveRows))
                .ToList();
            RaisePropertyChanged(nameof(FilterRows));
            RaisePropertyChanged(nameof(HasProfileFilters));
        }

        /// <summary>
        /// Rows of the current filter list replace their saved entries; entries of
        /// filters no longer in the list are kept, so a filter taken out of the wheel
        /// for a season keeps its description.
        /// </summary>
        private void SaveRows() {
            var rows = filterRows ?? Array.Empty<FilterAssignmentRow>();
            var shown = new HashSet<string>(rows.Select(r => r.NinaName), StringComparer.OrdinalIgnoreCase);
            options.Assignments = options.Assignments
                .Where(a => !shown.Contains(a.NinaName))
                .Concat(rows.Select(r => r.ToAssignment()).Where(a => a != null))
                .ToList();
        }

        private void TakeSuggestions() {
            foreach (var row in FilterRows) {
                row.TakeSuggestion();
            }
        }

        private void WatchFilterList() {
            if (watchedFilters != null) {
                watchedFilters.CollectionChanged -= OnFilterListChanged;
            }
            watchedFilters = profileService.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
            if (watchedFilters != null) {
                watchedFilters.CollectionChanged += OnFilterListChanged;
            }
        }

        private void OnFilterListChanged(object sender, NotifyCollectionChangedEventArgs e) => BuildRows();

        public double SkyQuality {
            get => options.SkyQuality;
            set {
                options.SkyQuality = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(NarrowbandFloorText));
            }
        }

        public double ThresholdBroadband {
            get => options.ThresholdBroadband;
            set {
                options.ThresholdBroadband = value;
                RaisePropertyChanged();
            }
        }

        public double ThresholdNarrowband {
            get => options.ThresholdNarrowband;
            set {
                options.ThresholdNarrowband = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(NarrowbandFloorText));
            }
        }

        public double MinimumAltitude {
            get => options.MinimumAltitude;
            set {
                options.MinimumAltitude = value;
                RaisePropertyChanged();
            }
        }

        public double OverheadSeconds {
            get => options.OverheadSeconds;
            set {
                options.OverheadSeconds = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// How dark the site must be for a typical 7 nm band to open at all - the
        /// answer to "why is nothing ever open here" before anyone has to ask.
        /// </summary>
        public string NarrowbandFloorText {
            get {
                var settings = options.ToWindowSettings();
                var floor3 = NightModel.MinimumSkyBrightness(NightModel.LineFilter("Ha", 3), settings);
                var floor7 = NightModel.MinimumSkyBrightness(NightModel.LineFilter("Ha", 7), settings);
                var floor12 = NightModel.MinimumSkyBrightness(NightModel.LineFilter("Ha", 12), settings);
                return Text.T("Options_Floors", floor3, floor7, floor12);
            }
        }

        private void OnProfileChanged(object sender, EventArgs e) {
            RaisePropertyChanged(nameof(SkyQuality));
            RaisePropertyChanged(nameof(ThresholdBroadband));
            RaisePropertyChanged(nameof(ThresholdNarrowband));
            RaisePropertyChanged(nameof(MinimumAltitude));
            RaisePropertyChanged(nameof(OverheadSeconds));
            RaisePropertyChanged(nameof(NarrowbandFloorText));
            WatchFilterList();
            BuildRows();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
