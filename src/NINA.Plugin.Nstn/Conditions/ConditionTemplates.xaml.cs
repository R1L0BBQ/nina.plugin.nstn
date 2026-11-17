using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.Nstn.Conditions {

    [Export(typeof(ResourceDictionary))]
    public partial class ConditionTemplates : ResourceDictionary {

        public ConditionTemplates() {
            InitializeComponent();
        }
    }
}
