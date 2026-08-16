using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class UtilityPage : Page
    {
        public List<ToolItem> AllTools { get; set; } = new List<ToolItem>();
        public ObservableCollection<ToolItem> FilteredTools { get; set; } = new ObservableCollection<ToolItem>();

        public UtilityPage()
        {
            this.InitializeComponent();
            InitializeTools();
        }

        private void InitializeTools()
        {
            AllTools.Clear();
            AllTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_FileHashPage/Content"),
                Description = LocalizationHelper.GetString("FileHashPage_Desc/Text"),
                Icon = "\uEC19",
                Tag = "FileHashPage",
                Category = LocalizationHelper.GetString("ToolHub_Tag_Utility/Text")
            });
            AllTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_FileAssociationPage/Content"),
                Description = LocalizationHelper.GetString("FileAssociationPage_Desc/Text"),
                Icon = "\uE7B5",
                Tag = "FileAssociationPage",
                Category = LocalizationHelper.GetString("ToolHub_Tag_Utility/Text")
            });

            ApplyFilter(string.Empty);
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            ApplyFilter(sender.Text);
        }

        private void ApplyFilter(string query)
        {
            FilteredTools.Clear();
            var trimmed = query.Trim();

            var matches = string.IsNullOrEmpty(trimmed)
                ? AllTools
                : AllTools.Where(t => t.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                     t.Description.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                     t.Category.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var item in matches)
            {
                FilteredTools.Add(item);
            }

            NoResultsPanel.Visibility = FilteredTools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolsGridView.Visibility = FilteredTools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
