using System;
using System.Collections.Generic;

namespace lanlanlu_toolkit.Views
{
    public class ToolItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();

        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;
            var q = query.Trim();
            if (Title.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (Description.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (Category.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var kw in Keywords)
            {
                if (kw.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
