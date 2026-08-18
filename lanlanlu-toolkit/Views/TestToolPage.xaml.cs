using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class TestToolPage : Page
    {
        public List<ToolItem> AllTools { get; set; } = new List<ToolItem>();
        public ObservableCollection<ToolItem> FilteredTools { get; set; } = new ObservableCollection<ToolItem>();

        public TestToolPage()
        {
            this.InitializeComponent();
            InitializeTools();
        }

        private void InitializeTools()
        {
            AllTools.Clear();
            AllTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_InputTesterPage/Content"),
                Description = LocalizationHelper.GetString("InputTesterPage_Subtitle/Text"),
                Icon = "\uE765",
                Tag = "InputTesterPage",
                Category = LocalizationHelper.GetString("ToolHub_Tag_Hardware/Text")
            });
            AllTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_CrashReportPage/Content"),
                Description = LocalizationHelper.GetString("CrashReportPage_Subtitle/Text"),
                Icon = "\uE7BA",
                Tag = "CrashReportPage",
                Category = LocalizationHelper.GetString("ToolHub_Tag_System/Text")
            });
            AllTools.Add(new ToolItem
            {
                Title = LocalizationHelper.GetString("Nav_SystemRepairPage/Content"),
                Description = LocalizationHelper.GetString("SystemRepairPage_AutoMode_Desc/Text"),
                Icon = "\uE762",
                Tag = "SystemRepairPage",
                Category = LocalizationHelper.GetString("ToolHub_Tag_System/Text")
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

        private void ToolsGridView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGridViewLayout(e.NewSize.Width);
        }

        private void UpdateGridViewLayout(double availableWidth)
        {
            if (ToolsGridView.ItemsPanelRoot is ItemsWrapGrid wrapGrid && availableWidth > 0)
            {
                // Dynamic responsive columns:
                // 3 columns when available width >= 780px (matching HomePage),
                // 2 columns when width >= 500px,
                // 1 column when narrower.
                int columns = availableWidth >= 780 ? 3 : (availableWidth >= 500 ? 2 : 1);
                wrapGrid.MaximumRowsOrColumns = columns;
                wrapGrid.ItemWidth = Math.Floor(availableWidth / columns);
            }
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
