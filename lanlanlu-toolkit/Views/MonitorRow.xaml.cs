using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class MonitorRow : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(MonitorRow), new PropertyMetadata(string.Empty, (d, e) =>
            {
                if (d is MonitorRow row) row.LabelText.Text = e.NewValue as string;
            }));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(MonitorRow), new PropertyMetadata(0.0, (d, e) =>
            {
                if (d is MonitorRow row)
                {
                    row.UpdateBarWidth(true);
                    row.UpdateValueText();
                }
            }));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string ValueSuffix { get; set; } = string.Empty;
        public double MaxValue { get; set; } = 100.0;

        public MonitorRow()
        {
            this.InitializeComponent();
            this.SizeChanged += (s, e) => UpdateBarWidth(false);
        }

        public void Update(double value, string? customText = null)
        {
            Value = value;
            
            if (customText != null)
            {
                ValueText.Text = customText;
            }
            // If customText is null, UpdateValueText() called by ValueProperty callback will handle it
        }

        private void UpdateValueText()
        {
            string format = (MaxValue > 500) ? "F0" : "F1";
            ValueText.Text = $"{Value.ToString(format)}{ValueSuffix}";
        }

        private void UpdateBarWidth(bool animate)
        {
            double ratio = Value / MaxValue;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            double targetWidth = BarContainer.ActualWidth * ratio;
            
            if (animate)
            {
                BarWidthAnimation.To = targetWidth;
                BarAnimation.Begin();
            }
            else
            {
                BarAnimation.Stop();
                BarFill.Width = targetWidth;
            }
        }
    }
}
