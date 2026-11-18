using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Reflection;

/// <summary>
/// TEST AID, never shipped. The .NET runtime calls this before N.I.N.A.'s Main
/// when DOTNET_STARTUP_HOOKS points at this DLL (the same seam as NINA.Benchmark/StartupProbe
/// in N.I.N.A. develop). It moves everything N.I.N.A. keeps per user into NINA_TEST_DATA:
/// profiles, plugins, logs, caches (APPLICATIONTEMPPATH) and the database (%localappdata%,
/// which the launcher points at the same folder). The application settings stay in memory,
/// so the user.config of the installed N.I.N.A. is neither read nor written.
/// Anything unexpected ends the process: a test instance must never fall back to the real data.
/// </summary>
public static class StartupHook {

    public static void Initialize() {
        var dataDir = Environment.GetEnvironmentVariable("NINA_TEST_DATA");
        if (string.IsNullOrWhiteSpace(dataDir)) {
            Environment.FailFast("NinaTestHook: NINA_TEST_DATA is not set.");
        }
        dataDir = Path.GetFullPath(dataDir);
        var expanded = Path.GetFullPath(Environment.ExpandEnvironmentVariables(@"%localappdata%\NINA"));
        if (!string.Equals(expanded, dataDir, StringComparison.OrdinalIgnoreCase)) {
            Environment.FailFast($"NinaTestHook: %localappdata%\\NINA is {expanded}, not {dataDir}.");
        }

        try {
            Directory.CreateDirectory(dataDir);
            var core = Assembly.Load("NINA.Core");
            var field = core.GetType("NINA.Core.Utility.CoreUtil", true).GetField("APPLICATIONTEMPPATH")
                ?? throw new MissingFieldException("CoreUtil.APPLICATIONTEMPPATH");
            field.SetValue(null, dataDir);
            if (!string.Equals((string)field.GetValue(null), dataDir, StringComparison.Ordinal)) {
                throw new InvalidOperationException("APPLICATIONTEMPPATH did not take the new value.");
            }

            var app = Assembly.Load("NINA");
            var settings = (ApplicationSettingsBase)app.GetType("NINA.Properties.Settings", true)
                .GetProperty("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var provider = new InMemorySettingsProvider();
            provider.Initialize("NinaTestHook", new NameValueCollection());
            settings.Providers.Clear();
            settings.Providers.Add(provider);
            foreach (SettingsProperty property in settings.Properties) {
                property.Provider = provider;
            }
            settings.Reload();
            settings["UpdateSettings"] = false;
            settings["UseSavedProfileSelection"] = true;
            settings["DatabaseLocation"] = Path.Combine(dataDir, "NINA.sqlite");
            var log = Path.Combine(dataDir, "nina-test-hook.log");
            File.AppendAllText(log,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} pid {Environment.ProcessId}: data {dataDir}, settings in memory{Environment.NewLine}");

            // NINA_TEST_NSTN_CLOCK (UTC, ISO 8601): NSTN's clock runs on from that instant once
            // the plugin is loaded, so that its windows are open in a daytime test. Only this
            // test aid moves it, by reflection; the plugin has no setting or variable for it.
            var clock = Environment.GetEnvironmentVariable("NINA_TEST_NSTN_CLOCK");
            if (!string.IsNullOrWhiteSpace(clock)) {
                var startUtc = DateTime.Parse(clock, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                AppDomain.CurrentDomain.AssemblyLoad += (_, args) => {
                    if (args.LoadedAssembly.GetName().Name != "NINA.Plugin.Nstn") {
                        return;
                    }
                    try {
                        var runFrom = args.LoadedAssembly.GetType("NINA.Plugin.Nstn.NstnClock", true)
                            .GetMethod("RunFrom", BindingFlags.Static | BindingFlags.NonPublic)
                            ?? throw new MissingMethodException("NstnClock.RunFrom");
                        runFrom.Invoke(null, new object[] { startUtc });
                        File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} pid {Environment.ProcessId}: NSTN clock runs from {startUtc:u}{Environment.NewLine}");
                    } catch (Exception ex) {
                        Environment.FailFast("NinaTestHook: NSTN clock: " + ex);
                    }
                };
            }
        } catch (Exception ex) {
            Environment.FailFast("NinaTestHook: " + ex);
        }
    }

    /// <summary>Defaults on read, nothing written (as in N.I.N.A.'s startup benchmark).</summary>
    private sealed class InMemorySettingsProvider : SettingsProvider, IApplicationSettingsProvider {

        public override string ApplicationName { get; set; } = "NinaTestHook";

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties) {
            var values = new SettingsPropertyValueCollection();
            foreach (SettingsProperty property in properties) {
                values.Add(new SettingsPropertyValue(property) { SerializedValue = property.DefaultValue, IsDirty = false });
            }
            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values) {
        }

        public SettingsPropertyValue GetPreviousVersion(SettingsContext context, SettingsProperty property) => null;

        public void Reset(SettingsContext context) {
        }

        public void Upgrade(SettingsContext context, SettingsPropertyCollection properties) {
        }
    }
}
