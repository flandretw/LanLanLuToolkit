using System;
using Microsoft.UI.Xaml.Controls;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class GpuMonitorCard : UserControl
    {
        public GpuMonitorCard()
        {
            this.InitializeComponent();
        }

        public void Initialize(string name, int index)
        {
            var resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            GpuNameText.Text = name;
            GpuTitleText.Text = string.Format(resourceLoader.GetString("GpuMonitorCard_GpuTitle"), index);
        }

        public void InitializeDetails(string driverVersion, string driverDate, string directX, string location, double hardwareReserved)
        {
            GpuDriverVersionText.Text = driverVersion;
            GpuDriverDateText.Text = driverDate;
            GpuDirectXVersionText.Text = directX;
            GpuPhysicalLocationText.Text = location;
            GpuHardwareReservedText.Text = $"{hardwareReserved:F1} GB";
        }

        private readonly System.Collections.Generic.Queue<double> _history = new();
        private const int MaxHistory = 60;

        public void UpdateStats(double usage, double clock, double memClock, double temp)
        {
            GpuUsageRow.Update(usage);
            GpuClockText.Text = $"{clock:F0} MHz";
            GpuMemClockText.Text = $"{memClock:F0} MHz";
            GpuTempText.Text = $"{temp:F1} °C";
            UpdateChart(usage);
        }

        private void UpdateChart(double usage)
        {
            _history.Enqueue(usage);
            if (_history.Count > MaxHistory) _history.Dequeue();

            var points = new Microsoft.UI.Xaml.Media.PointCollection();
            int i = 0;
            foreach (var val in _history)
            {
                // Normalize points: X from 0 to MaxHistory, Y from 0 to 100
                // Note: In Polyline with Stretch="Fill", relative coordinates work best
                points.Add(new Windows.Foundation.Point(i, 100 - val));
                i++;
            }
            GpuPolyline.Points = points;
        }

        public void UpdateMemory(double dedicatedUsed, double dedicatedTotal, double sharedUsed, double sharedTotal)
        {
            double totalUsed = dedicatedUsed + sharedUsed;
            double totalCap = dedicatedTotal + sharedTotal;

            GpuDedicatedMemText.Text = $"{dedicatedUsed:F1} / {dedicatedTotal:F1} GB";
            GpuSharedMemText.Text = $"{sharedUsed:F1} / {sharedTotal:F1} GB";
            GpuTotalMemText.Text = $"{totalUsed:F1} / {totalCap:F1} GB";
        }
    }
}
