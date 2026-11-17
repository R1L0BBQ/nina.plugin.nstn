using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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

        [ImportingConstructor]
        public NstnPlugin(IProfileService profileService) {
            this.profileService = profileService;
            options = new NstnOptions(profileService);
            profileService.ProfileChanged += OnProfileChanged;
        }

        public override Task Teardown() {
            profileService.ProfileChanged -= OnProfileChanged;
            return base.Teardown();
        }

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
                return $"Narrowband bands open only where the site is at least this dark: 3 nm {floor3:0.00}, 7 nm {floor7:0.00}, 12 nm {floor12:0.00} mag/arcsec².";
            }
        }

        private void OnProfileChanged(object sender, EventArgs e) {
            RaisePropertyChanged(nameof(SkyQuality));
            RaisePropertyChanged(nameof(ThresholdBroadband));
            RaisePropertyChanged(nameof(ThresholdNarrowband));
            RaisePropertyChanged(nameof(MinimumAltitude));
            RaisePropertyChanged(nameof(OverheadSeconds));
            RaisePropertyChanged(nameof(NarrowbandFloorText));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
