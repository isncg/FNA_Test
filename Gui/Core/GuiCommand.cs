using System;

namespace FNA.Gui
{
    /// <summary>
    /// A command abstraction for decoupling UI actions from widget event handlers.
    /// <see cref="CanExecute"/> drives the Enabled state of bound widgets.
    /// </summary>
    public interface IGuiCommand
    {
        bool CanExecute();
        void Execute();
    }

    /// <summary>Concrete command implementation with delegate callbacks.</summary>
    public class GuiCommand : IGuiCommand
    {
        private readonly Func<bool> _canExecute;
        private readonly Action _execute;

        /// <summary>Fired when the CanExecute result may have changed.</summary>
        public event Action? CanExecuteChanged;

        public GuiCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (() => true);
        }

        public bool CanExecute() => _canExecute();

        public void Execute()
        {
            if (CanExecute())
                _execute();
        }

        /// <summary>Notify that CanExecute may have changed (call from external code).</summary>
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke();
    }
}
