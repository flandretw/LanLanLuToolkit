using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class TestToolPage : Page
    {
        public List<ToolItem> Tools { get; set; } = new List<ToolItem>();

        public TestToolPage()
        {
            this.InitializeComponent();
            InitializeTools();
        }

        private void InitializeTools()
        {
            Tools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_InputTesterPage/Content"),
                Description = LocalizationHelper.GetString("InputTesterPage_Subtitle/Text"),
                Icon = "\uE765",
                Tag = "InputTesterPage"
            });
            Tools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_CrashReportPage/Content"),
                Description = LocalizationHelper.GetString("CrashReportPage_Subtitle/Text"),
                Icon = "\uE7BA",
                Tag = "CrashReportPage"
            });
            Tools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_SystemRepairPage/Content"),
                Description = LocalizationHelper.GetString("SystemRepairPage_AutoMode_Desc/Text"),
                Icon = "\uE762",
                Tag = "SystemRepairPage"
            });
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                switch (tag)
                {
                    case "InputTesterPage":
                        this.Frame.Navigate(typeof(InputTesterPage));
                        break;
                    case "CrashReportPage":
                        this.Frame.Navigate(typeof(CrashReportPage));
                        break;
                    case "SystemRepairPage":
                        this.Frame.Navigate(typeof(SystemRepairPage));
                        break;
                }
            }
        }
    }
}
