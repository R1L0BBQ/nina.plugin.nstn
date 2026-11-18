using System;
using System.Globalization;
using System.Windows.Input;
using Newtonsoft.Json;
using NINA.Core.Utility;

namespace NINA.Plugin.Nstn.FilterPlan {

    /// <summary>
    /// One row of the filter plan: a filter as it is named in the profile, how many
    /// exposures per round and how long each one is. With the plan values the only
    /// thing the user edits; the instructions of the plan are built from them.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FilterPlanRow : BaseINPC {

        private string filterName = string.Empty;
        private int count = 5;
        private double exposureSeconds = 300;

        [JsonProperty]
        public string FilterName {
            get => filterName;
            set {
                // A ComboBox whose list is being swapped can push null; that is not a choice.
                if (value == null) {
                    return;
                }
                filterName = value.Trim();
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public int Count {
            get => count;
            set {
                // Keep the last valid value; a text box shows it again.
                if (value >= 1) {
                    count = value;
                }
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double ExposureSeconds {
            get => exposureSeconds;
            set {
                if (value > 0 && !double.IsInfinity(value)) {
                    exposureSeconds = value;
                }
                RaisePropertyChanged();
            }
        }

        public FilterPlanRow Copy() => new FilterPlanRow { FilterName = FilterName, Count = Count, ExposureSeconds = ExposureSeconds };

        public string Label => $"{FilterName}: {Count} × {ExposureSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s";
    }

    /// <summary>A button command with a parameter, always executable.</summary>
    internal sealed class ParameterCommand<T> : ICommand {

        private readonly Action<T> action;

        public ParameterCommand(Action<T> action) {
            this.action = action;
        }

        public event EventHandler CanExecuteChanged {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) {
            if (parameter is T value) {
                action(value);
            }
        }
    }
}
