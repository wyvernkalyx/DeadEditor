using DeadEditor.Models;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfBrush = System.Windows.Media.Brush;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace DeadEditor.Controls
{
    /// <summary>
    /// Horizontal row of pill-shaped status indicators visualizing the canonical
    /// Import flow (Load → Enrich → Clean → Structure → Import). Read-only signage:
    /// no click-to-execute, no enforcement. Drives no business logic; the stages
    /// are computed externally by
    /// <see cref="DeadEditor.Services.ImportWorkflowState"/>.
    /// </summary>
    public partial class WorkflowStepper : WpfUserControl
    {
        public static readonly DependencyProperty StagesProperty = DependencyProperty.Register(
            nameof(Stages),
            typeof(IEnumerable<WorkflowStage>),
            typeof(WorkflowStepper),
            new PropertyMetadata(null, OnStagesChanged));

        public IEnumerable<WorkflowStage>? Stages
        {
            get => (IEnumerable<WorkflowStage>?)GetValue(StagesProperty);
            set => SetValue(StagesProperty, value);
        }

        public WorkflowStepper()
        {
            InitializeComponent();
        }

        private static void OnStagesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var stepper = (WorkflowStepper)d;
            stepper.Unsubscribe(e.OldValue as IEnumerable<WorkflowStage>);
            stepper.Subscribe(e.NewValue as IEnumerable<WorkflowStage>);
            stepper.Rebuild();
        }

        private void Subscribe(IEnumerable<WorkflowStage>? stages)
        {
            if (stages is INotifyCollectionChanged ncc)
                ncc.CollectionChanged += OnStagesCollectionChanged;
            if (stages != null)
            {
                foreach (var stage in stages)
                    stage.PropertyChanged += OnStagePropertyChanged;
            }
        }

        private void Unsubscribe(IEnumerable<WorkflowStage>? stages)
        {
            if (stages is INotifyCollectionChanged ncc)
                ncc.CollectionChanged -= OnStagesCollectionChanged;
            if (stages != null)
            {
                foreach (var stage in stages)
                    stage.PropertyChanged -= OnStagePropertyChanged;
            }
        }

        private void OnStagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Resubscribe to per-stage PropertyChanged for added/removed items so
            // a fresh Compute() result keeps wiring up correctly without forcing
            // callers to reset the DP.
            if (e.OldItems != null)
            {
                foreach (WorkflowStage stage in e.OldItems)
                    stage.PropertyChanged -= OnStagePropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (WorkflowStage stage in e.NewItems)
                    stage.PropertyChanged += OnStagePropertyChanged;
            }
            Rebuild();
        }

        private void OnStagePropertyChanged(object? sender, PropertyChangedEventArgs e)
            => Rebuild();

        private void Rebuild()
        {
            StepperPanel.Children.Clear();
            if (Stages == null) return;

            var list = Stages.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                StepperPanel.Children.Add(BuildPill(list[i]));
                if (i < list.Count - 1)
                    StepperPanel.Children.Add(BuildConnector(list[i].IsCompleted));
            }
        }

        private FrameworkElement BuildPill(WorkflowStage stage)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 5, 12, 5),
                MinHeight = 26,
                VerticalAlignment = VerticalAlignment.Center
            };

            var content = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (stage.IsCompleted)
            {
                border.Background = (WpfBrush)Resources["PillCompletedBg"];
                content.Children.Add(MakeRun("✓ ", "PillCompletedFg", FontWeights.Normal));
                content.Children.Add(MakeRun(stage.Name, "PillCompletedFg", FontWeights.Normal));
            }
            else if (stage.IsCurrent)
            {
                border.Background = (WpfBrush)Resources["PillCurrentBg"];
                content.Children.Add(MakeRun(stage.Name, "PillCurrentFg", FontWeights.SemiBold));
                content.Children.Add(MakeRun(" →", "PillCurrentFg", FontWeights.SemiBold));
            }
            else if (stage.IsSkipped)
            {
                border.Background = (WpfBrush)Resources["PillUpcomingBg"];
                content.Children.Add(MakeRun("! ", "PillSkippedAccent", FontWeights.Bold));
                content.Children.Add(MakeRun(stage.Name, "PillSkippedFg", FontWeights.Normal));
            }
            else // Upcoming
            {
                border.Background = (WpfBrush)Resources["PillUpcomingBg"];
                content.Children.Add(MakeRun(stage.Name, "PillUpcomingFg", FontWeights.Normal));
            }

            border.Child = content;
            return border;
        }

        private TextBlock MakeRun(string text, string brushKey, System.Windows.FontWeight weight)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = (WpfBrush)Resources[brushKey],
                FontSize = 13,
                FontWeight = weight,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private FrameworkElement BuildConnector(bool leftPillCompleted)
        {
            return new WpfRectangle
            {
                Width = 14,
                Height = 2,
                Fill = (WpfBrush)Resources[leftPillCompleted ? "ConnectorCompletedBg" : "ConnectorDimBg"],
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }
    }
}
