using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Text.RegularExpressions;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace DeadEditor
{
    /// <summary>
    /// Multi-step wizard for authoring a <see cref="BoxSetDefinition"/>. Single
    /// UserControl; step transitions toggle <c>Visibility</c> on four content panels
    /// rather than navigating between views. Step 1 (top-level info) ships in this
    /// commit; steps 2-4 are placeholders.
    ///
    /// Shell-agnostic by design: raises <see cref="Completed"/> (Save success or
    /// Cancel) and <see cref="StepChanged"/> for the ShellWindow to plumb back to
    /// navigation and the HeaderBar's Back/Next/Save button state.
    /// </summary>
    public partial class BoxSetWizardView : System.Windows.Controls.UserControl
    {
        private readonly BoxSetService _boxSetService;
        private readonly BoxSetDefinition _definition = new();
        private int _currentStep = 1;

        // yyyy-MM-dd validation regex — same pattern as EditSetlistView.xaml.cs:197.
        private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

        /// <summary>The currently visible step (1-4).</summary>
        public int CurrentStep => _currentStep;

        /// <summary>Fired when the wizard finishes — either Save succeeded or the user
        /// cancelled. ShellWindow uses this to navigate back to the Box Sets list.</summary>
        public event EventHandler? Completed;

        /// <summary>Fired whenever the visible step changes. ShellWindow forwards the new
        /// step to <c>HeaderBar.UpdateBoxSetWizardStep</c> so Back/Next/Save buttons update.</summary>
        public event EventHandler<int>? StepChanged;

        public BoxSetWizardView(BoxSetService boxSetService)
        {
            InitializeComponent();
            _boxSetService = boxSetService;
            ShowStep(1);
        }

        // ===== STEP NAVIGATION (called from HeaderBar via ShellWindow) =====

        /// <summary>Advances to the next step if the current step's validation passes.
        /// Step 1 has real validation; later steps' content is a placeholder and always
        /// passes.</summary>
        public void GoNext()
        {
            if (_currentStep == 1 && !ValidateStep1())
                return;

            if (_currentStep < 4)
                ShowStep(_currentStep + 1);
        }

        /// <summary>Moves back one step. No validation — back-navigation always allowed.</summary>
        public void GoBack()
        {
            if (_currentStep > 1)
                ShowStep(_currentStep - 1);
        }

        /// <summary>
        /// Validates step 1, checks for slug collision against existing definitions, and
        /// writes via <see cref="BoxSetService.Write"/>. On any failure surfaces the error
        /// (jumping back to step 1 so the user can see the in-form ValidationMessage) and
        /// does not raise Completed. On success raises Completed so ShellWindow navigates
        /// back to the list view, which reloads and shows the new definition.
        /// </summary>
        public void Save()
        {
            if (!ValidateStep1())
            {
                if (_currentStep != 1) ShowStep(1);
                return;
            }

            var slug = BoxSetService.DeriveSlug(_definition.Name);

            // Collision check — Write would silently overwrite. Surface as a validation
            // error so the user picks a different name. Edit-existing (commit 6) will use
            // a different code path that does want to overwrite.
            if (_boxSetService.Read(slug) != null)
            {
                ValidationMessage.Text = "A box set with this name already exists. Please use a different name.";
                if (_currentStep != 1) ShowStep(1);
                return;
            }

            try
            {
                _boxSetService.Write(_definition, slug);
            }
            catch (Exception ex)
            {
                // Pattern from EditSetlistView.xaml.cs:296-300 — surface as MessageBox,
                // do not raise Completed.
                MessageBox.Show($"Error saving box set:\n\n{ex.Message}", "Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Completed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Discards the in-progress definition and signals Completed. No
        /// confirmation prompt (out of scope per the commit brief).</summary>
        public void Cancel()
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }

        // ===== STEP SWITCHING =====

        private void ShowStep(int step)
        {
            _currentStep = step;

            Step1Content.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Content.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Content.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            Step4Content.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

            StepIndicatorText.Text = step switch
            {
                1 => "Step 1 of 4 — Top-Level Info",
                2 => "Step 2 of 4 — Concerts",
                3 => "Step 3 of 4 — Discs",
                4 => "Step 4 of 4 — Review",
                _ => $"Step {step} of 4"
            };

            StepChanged?.Invoke(this, step);
        }

        // ===== STEP 1 VALIDATION =====

        /// <summary>
        /// Reads step-1 field text into <see cref="_definition"/> and validates required
        /// fields + formats. Returns true on success; on failure sets
        /// <see cref="ValidationMessage"/> to the first error message and returns false.
        /// Reads on demand rather than via per-field LostFocus handlers (matches
        /// EditSetlistView.xaml.cs:212-219's read-on-save pattern).
        /// </summary>
        private bool ValidateStep1()
        {
            SyncStep1FieldsToDefinition();
            ValidationMessage.Text = "";

            if (string.IsNullOrWhiteSpace(_definition.Name))
            {
                ValidationMessage.Text = "Name is required.";
                return false;
            }

            if (!DateRegex.IsMatch(_definition.ReleaseDate))
            {
                ValidationMessage.Text = "Release Date must be in yyyy-MM-dd format.";
                return false;
            }

            if (_definition.DiscCount < 1 || _definition.DiscCount > 99)
            {
                ValidationMessage.Text = "Disc Count must be between 1 and 99.";
                return false;
            }

            return true;
        }

        /// <summary>Copies step-1 TextBox values into <see cref="_definition"/>. Trimming
        /// applied so trailing whitespace doesn't leak into the slug or saved JSON.</summary>
        private void SyncStep1FieldsToDefinition()
        {
            _definition.Name = NameTextBox.Text?.Trim() ?? "";
            _definition.ReleaseDate = ReleaseDateTextBox.Text?.Trim() ?? "";
            _definition.Label = LabelTextBox.Text?.Trim() ?? "";
            _definition.CatalogNumber = CatalogNumberTextBox.Text?.Trim() ?? "";
            _definition.Notes = NotesTextBox.Text?.Trim() ?? "";

            // int.TryParse leaves the out value at 0 on failure, which is intentionally
            // out-of-range so validation catches it as "must be between 1 and 99."
            _ = int.TryParse(DiscCountTextBox.Text?.Trim(), out var dc);
            _definition.DiscCount = dc;
        }
    }
}
