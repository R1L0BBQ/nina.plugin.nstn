using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.Nstn.Instructions {

    [Export(typeof(ResourceDictionary))]
    public partial class InstructionTemplates : ResourceDictionary {

        public InstructionTemplates() {
            InitializeComponent();
        }
    }
}
