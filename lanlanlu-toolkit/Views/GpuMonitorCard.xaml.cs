using System;
using Microsoft.UI.Xaml.Controls;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class GpuMonitorCard : UserControl
    {
        private readonly Microsoft.Windows.ApplicationModel.Resources.ResourceLoader _resources = new();

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
            GpuTitleText.Text = string.Format(_resources.GetString("GpuMonitorCard_GpuTitle"), index);
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
            // 寬度固定為 300，MaxHistory 為 60，所以每點間距 5px
            double step = 300.0 / (MaxHistory - 1);

            foreach (var val in _history)
            {
                // Y 軸高度為 160，百分比換算 (100-val)/100 * 160
                double y = (100 - val) / 100.0 * 160.0;
                points.Add(new Windows.Foundation.Point(i * step, y));
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
