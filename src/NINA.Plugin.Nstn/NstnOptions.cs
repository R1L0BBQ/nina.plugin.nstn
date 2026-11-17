using System;
using System.Collections.Generic;
using NINA.Profile;
using NINA.Profile.Interfaces;
using Nstn.Core;

namespace NINA.Plugin.Nstn {

    /// <summary>
    /// The plugin settings of the active NINA profile. Read on every access, so a
    /// profile switch takes effect without restarting anything.
    /// </summary>
    public sealed class NstnOptions {

        // 0 means "not set": the model needs the site's own sky quality and there
        // is no sensible default for it - a guess would silently move every window.
        public const double UnsetSkyQuality = 0.0;

        private static readonly Guid PluginGuid =
            PluginOptionsAccessor.GetAssemblyGuid(typeof(NstnOptions)) ?? throw new InvalidOperationException("NSTN plugin assembly has no GUID");

        private readonly PluginOptionsAccessor accessor;

        public NstnOptions(IProfileService profileService) {
            accessor = new PluginOptionsAccessor(profileService, PluginGuid);
        }

        /// <summary>Zenith sky quality of the site in mag/arcsec² (an SQM reading).</summary>
        public double SkyQuality {
            get => accessor.GetValueDouble(nameof(SkyQuality), UnsetSkyQuality);
            set => accessor.SetValueDouble(nameof(SkyQuality), value);
        }

        public double ThresholdBroadband {
            get => accessor.GetValueDouble(nameof(ThresholdBroadband), NightModel.DefaultSettings.ThresholdBroadband);
            set => accessor.SetValueDouble(nameof(ThresholdBroadband), value);
        }

        public double ThresholdNarrowband {
            get => accessor.GetValueDouble(nameof(ThresholdNarrowband), NightModel.DefaultSettings.ThresholdNarrowband);
            set => accessor.SetValueDouble(nameof(ThresholdNarrowband), value);
        }

        public double MinimumAltitude {
            get => accessor.GetValueDouble(nameof(MinimumAltitude), NightModel.DefaultSettings.MinAltitudeDeg);
            set => accessor.SetValueDouble(nameof(MinimumAltitude), value);
        }

        /// <summary>
        /// Time added after every exposure in the night plan: download, saving,
        /// dithering on average. Only the estimate uses it, never a decision.
        /// </summary>
        public double OverheadSeconds {
            get => accessor.GetValueDouble(nameof(OverheadSeconds), 10.0);
            set => accessor.SetValueDouble(nameof(OverheadSeconds), Math.Max(0, value));
        }

        /// <summary>What each filter of the profile passes, for "this block's filter".</summary>
        public IReadOnlyList<FilterAssignment> Assignments {
            get => FilterAssignments.Deserialize(accessor.GetValueString(nameof(Assignments), string.Empty));
            set => accessor.SetValueString(nameof(Assignments), FilterAssignments.Serialize(value));
        }

        public bool HasSkyQuality => SkyQuality > UnsetSkyQuality;

        public WindowSettings ToWindowSettings() => new WindowSettings(
            ThresholdBroadband: ThresholdBroadband,
            ThresholdNarrowband: ThresholdNarrowband,
            MinAltitudeDeg: MinimumAltitude,
            GapMinutes: NightModel.DefaultSettings.GapMinutes);
    }
}
