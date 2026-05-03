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
            GpuNameText.Text = name;
            GpuTitleText.Text = $"顯示卡 (GPU {index})";
        }

        public void UpdateStats(double usage, double clock, double memClock, double temp)
        {
            GpuUsageRow.Update(usage);
            GpuClockRow.Update(clock);
            GpuMemClockRow.Update(memClock);
            GpuTempRow.Update(temp);
        }
    }
}
