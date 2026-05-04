using System;
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
                    row.RefreshVisuals();
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
            this.Loaded += (s, e) => RefreshVisuals();
            this.SizeChanged += (s, e) => RefreshVisuals();
        }

        public void Update(double value, string? customText = null)
        {
            try
            {
                // Ensure we don't update if the control is being destroyed
                if (this.DispatcherQueue == null) return;
                
                Value = value;
                
                if (customText != null)
                {
                    ValueText.Text = customText;
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Silently ignore COM exceptions during shutdown
            }
            catch (ObjectDisposedException)
            {
                // Silently ignore if object is already disposed
            }
        }

        private void RefreshVisuals()
        {
            if (BarContainer == null || BarContainer.ActualWidth <= 0) return;

            // 同步遮罩基準寬度
            ClipMask.Rect = new Windows.Foundation.Rect(0, 0, BarContainer.ActualWidth, 14);

            // 計算比例並執行動畫
            double ratio = Math.Clamp(Value / MaxValue, 0, 1);
            AnimateMask(ratio);
        }

        private void AnimateMask(double newScale)
        {
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = newScale,
                Duration = TimeSpan.FromMilliseconds(450),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, ClipScale);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "ScaleX");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        private void UpdateValueText()
        {
            string format = (MaxValue > 500) ? "F0" : "F1";
            ValueText.Text = $"{Value.ToString(format)}{ValueSuffix}";
        }
    }
}
