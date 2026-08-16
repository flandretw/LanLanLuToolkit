using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using lanlanlu_toolkit.Services;

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
            Tools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_FileHashPage/Content"),
                Description = LocalizationHelper.GetString("FileHashPage_Desc/Text"),
                Icon = "\uEC19",
                Tag = "FileHashPage"
            });
            Tools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_FileAssociationPage/Content"),
                Description = LocalizationHelper.GetString("FileAssociationPage_Desc/Text"),
                Icon = "\uE7B5",
                Tag = "FileAssociationPage"
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
                    case "FileAssociationPage":
                        this.Frame.Navigate(typeof(FileAssociationPage));
                        break;
                }
            }
        }
    }
}
