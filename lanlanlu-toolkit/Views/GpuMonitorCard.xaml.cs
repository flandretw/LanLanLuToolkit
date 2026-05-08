using System;
using Microsoft.UI.Xaml.Controls;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class GpuMonitorCard : UserControl
    {
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
            GpuTitleText.Text = string.Format(LocalizationHelper.GetString("GpuMonitorCard_GpuTitle"), index);
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
            
            if (SettingsService.GetTemperatureUnit() == "Fahrenheit")
            {
                double f = (temp * 9 / 5) + 32;
                GpuTempText.Text = string.Format(LocalizationHelper.GetString("Temperature_Fahrenheit_Format"), f);
            }
            else
            {
                GpuTempText.Text = string.Format(LocalizationHelper.GetString("Temperature_Celsius_Format"), temp);
            }
            
            UpdateChart(usage);
        }

        private void UpdateChart(double usage)
        {
            _history.Enqueue(usage);
            if (_history.Count > MaxHistory) _history.Dequeue();

            double w = GpuChartContainer.ActualWidth;
            double h = GpuChartContainer.ActualHeight;
            if (w <= 0) w = 300;
            if (h <= 0) h = 160;

            var linePoints = new Microsoft.UI.Xaml.Media.PointCollection();
            var fillPoints = new Microsoft.UI.Xaml.Media.PointCollection();
            double step = w / (MaxHistory - 1);

            var historyArray = _history.ToArray();

            // 1. Polyline & Polygon Base: Left to Right
            for (int i = 0; i < historyArray.Length; i++)
            {
                double y = (100 - historyArray[i]) / 100.0 * h;
                var p = new Windows.Foundation.Point(i * step, y);
                linePoints.Add(p);
                fillPoints.Add(p);
            }

            // Close the polygon for fill area (Bottom-right then Bottom-left)
            fillPoints.Add(new Windows.Foundation.Point(w, h));
            fillPoints.Add(new Windows.Foundation.Point(0, h));

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
