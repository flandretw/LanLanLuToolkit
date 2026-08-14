using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using Microsoft.Windows.ApplicationModel.Resources;

namespace lanlanlu_toolkit.Views
{
    public class ToolItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
    }

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
            // Use ResourceLoader directly in WinUI 3
            var resourceLoader = new ResourceLoader();
            Tools.Add(new ToolItem
            {
                Title = resourceLoader.GetString("Nav_CrashReportPage/Content"),
                Description = resourceLoader.GetString("CrashReportPage_Subtitle/Text"),
                Icon = "\uE7BA",
                Tag = "CrashReportPage"
            });
            Tools.Add(new ToolItem
            {
                Title = resourceLoader.GetString("Nav_SystemRepairPage/Content"),
                Description = resourceLoader.GetString("SystemRepairPage_AutoMode_Desc/Text"),
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
