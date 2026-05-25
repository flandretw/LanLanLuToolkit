using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using Microsoft.Windows.ApplicationModel.Resources;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class UtilityPage : Page
    {
        public List<ToolItem> Tools { get; set; } = new List<ToolItem>();

        public UtilityPage()
        {
            this.InitializeComponent();
            InitializeTools();
        }

        private void InitializeTools()
        {
            var resourceLoader = new ResourceLoader();
            Tools.Add(new ToolItem
            {
                Title = resourceLoader.GetString("Nav_FileHashPage/Content"),
                Description = resourceLoader.GetString("FileHashPage_Desc/Text"),
                Icon = "\uEC19",
                Tag = "FileHashPage"
            });
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                switch (tag)
                {
                    case "FileHashPage":
                        this.Frame.Navigate(typeof(FileHashPage));
                        break;
                }
            }
        }
    }
}
