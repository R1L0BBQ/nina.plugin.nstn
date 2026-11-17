using System;
using System.Windows.Input;

namespace NINA.Plugin.Nstn {

    /// <summary>A button command without parameter and without CanExecute logic.</summary>
    internal sealed class ActionCommand : ICommand {

        private readonly Action action;

        public ActionCommand(Action action) {
            this.action = action;
        }

        // Never raised: the command is always executable.
        public event EventHandler CanExecuteChanged {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => action();
    }
}
