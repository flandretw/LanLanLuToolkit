using System;
using Microsoft.UI.Xaml.Controls;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class GpuMonitorCard : UserControl
    {
        private Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _resources;
        private Microsoft.Windows.ApplicationModel.Resources.ResourceLoader AppResources => _resources ??= new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();

        public GpuMonitorCard()
        {
            this.InitializeComponent();
            InitializeHistory();
        }

        private void InitializeHistory()
        {
            _history.Clear();
            for (int i = 0; i < MaxHistory; i++)
            {
                _history.Enqueue(0);
            }
        }

        public void Initialize(string name, int index)
        {
            GpuNameText.Text = name;
            GpuTitleText.Text = string.Format(AppResources.GetString("GpuMonitorCard_GpuTitle"), index);
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

            var linePoints = new Microsoft.UI.Xaml.Media.PointCollection();
            var fillPoints = new Microsoft.UI.Xaml.Media.PointCollection();
            int i = 0;
            double step = 300.0 / (MaxHistory - 1);

            foreach (var val in _history)
            {
                double y = (100 - val) / 100.0 * 160.0;
                var p = new Windows.Foundation.Point(i * step, y);
                linePoints.Add(p);
                fillPoints.Add(p);
                i++;
            }

            // Close the polygon for fill area
            fillPoints.Add(new Windows.Foundation.Point(300, 160));
            fillPoints.Add(new Windows.Foundation.Point(0, 160));

            GpuPolyline.Points = linePoints;
            GpuPolygon.Points = fillPoints;
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
