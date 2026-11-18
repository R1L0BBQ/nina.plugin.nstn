using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.Nstn.FilterPlan {

    [Export(typeof(ResourceDictionary))]
    public partial class FilterPlanTemplates : ResourceDictionary {

        public FilterPlanTemplates() {
            InitializeComponent();
        }
    }
}
