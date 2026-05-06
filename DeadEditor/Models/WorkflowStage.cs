using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeadEditor.Models
{
    /// <summary>
    /// One stage in the Import view's workflow stepper. The stepper is purely
    /// informational signage: it visualizes progress through Load → Enrich →
    /// Clean → Structure → Import without enforcing order. State is derived by
    /// <see cref="DeadEditor.Services.ImportWorkflowState"/> from cheap heuristics
    /// on the loaded folder.
    /// </summary>
    public class WorkflowStage : INotifyPropertyChanged
    {
        private string _name = "";
        private bool _isCompleted;
        private bool _isCurrent;
        private bool _isSkipped;

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsUpcoming));
                }
            }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent != value)
                {
                    _isCurrent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsUpcoming));
                }
            }
        }

        public bool IsSkipped
        {
            get => _isSkipped;
            set
            {
                if (_isSkipped != value)
                {
                    _isSkipped = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsUpcoming));
                }
            }
        }

        public bool IsUpcoming => !IsCompleted && !IsCurrent && !IsSkipped;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
