using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.System;

namespace lanlanlu_toolkit.Services
{
    public enum KeyVisualState
    {
        Default,
        Pressed,
        Tested,
        Warning
    }

    public class KeyModel : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayLabel { get; set; } = string.Empty;
        public string SubLabel { get; set; } = string.Empty;
        public double Width { get; set; } = 42;
        public double Height { get; set; } = 42;
        public double LeftMargin { get; set; } = 0;
        public int Row { get; set; } = 0;
        public int Column { get; set; } = 0;
        public int RowSpan { get; set; } = 1;
        public int ColumnSpan { get; set; } = 1;
        public VirtualKey PrimaryKey { get; set; } = VirtualKey.None;
        public int ScanCode { get; set; } = 0;
        public bool IsSpacer { get; set; } = false;

        private KeyVisualState _state = KeyVisualState.Default;
        public KeyVisualState State
        {
            get => _state;
            set { if (_state != value) { _state = value; OnPropertyChanged(); } }
        }

        private int _pressCount = 0;
        public int PressCount
        {
            get => _pressCount;
            set { if (_pressCount != value) { _pressCount = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum KeyboardLayoutType
    {
        Full104 = 0,
        Tkl87 = 1,
        Compact61 = 2
    }

    public class KeyboardClusters
    {
        public List<List<KeyModel>> MainClusterRows { get; set; } = new();
        public List<List<KeyModel>> NavClusterRows { get; set; } = new();
        public List<KeyModel> NumpadGridKeys { get; set; } = new();
    }

    public static class KeyboardLayoutProvider
    {
        public static KeyboardClusters GenerateClusters(KeyboardLayoutType layoutType = KeyboardLayoutType.Full104)
        {
            var clusters = new KeyboardClusters();

            // =========================================================================
            // 1. MAIN ALPHANUMERIC CLUSTER (15.0 Units total row width = 714px)
            // =========================================================================
            if (layoutType != KeyboardLayoutType.Compact61)
            {
                // Row 0: Esc (42) + Gap(32) + F1-F4 (186) + Gap(32) + F5-F8 (186) + Gap(32) + F9-F12 (186) = 714px (Aligned to F12 right border)
                var mainRow0 = new List<KeyModel>
                {
                    new() { Id = "Escape", DisplayLabel = "Esc", Width = 42, PrimaryKey = VirtualKey.Escape },
                    new() { Id = "F1", DisplayLabel = "F1", Width = 42, LeftMargin = 32, PrimaryKey = VirtualKey.F1 },
                    new() { Id = "F2", DisplayLabel = "F2", Width = 42, PrimaryKey = VirtualKey.F2 },
                    new() { Id = "F3", DisplayLabel = "F3", Width = 42, PrimaryKey = VirtualKey.F3 },
                    new() { Id = "F4", DisplayLabel = "F4", Width = 42, PrimaryKey = VirtualKey.F4 },
                    new() { Id = "F5", DisplayLabel = "F5", Width = 42, LeftMargin = 32, PrimaryKey = VirtualKey.F5 },
                    new() { Id = "F6", DisplayLabel = "F6", Width = 42, PrimaryKey = VirtualKey.F6 },
                    new() { Id = "F7", DisplayLabel = "F7", Width = 42, PrimaryKey = VirtualKey.F7 },
                    new() { Id = "F8", DisplayLabel = "F8", Width = 42, PrimaryKey = VirtualKey.F8 },
                    new() { Id = "F9", DisplayLabel = "F9", Width = 42, LeftMargin = 32, PrimaryKey = VirtualKey.F9 },
                    new() { Id = "F10", DisplayLabel = "F10", Width = 42, PrimaryKey = VirtualKey.F10 },
                    new() { Id = "F11", DisplayLabel = "F11", Width = 42, PrimaryKey = VirtualKey.F11 },
                    new() { Id = "F12", DisplayLabel = "F12", Width = 42, PrimaryKey = VirtualKey.F12 }
                };
                clusters.MainClusterRows.Add(mainRow0);
            }

            // Row 1: `~, 1-0, -, =, Backspace (2.0U = 90px) -> Total = 714px
            var firstKeyOfRow1 = (layoutType == KeyboardLayoutType.Compact61)
                ? new KeyModel { Id = "Escape", DisplayLabel = "Esc", SubLabel = "`", Width = 42, PrimaryKey = VirtualKey.Escape }
                : new KeyModel { Id = "Backquote", DisplayLabel = "`", SubLabel = "~", Width = 42, PrimaryKey = (VirtualKey)192 };
            var mainRow1 = new List<KeyModel>
            {
                firstKeyOfRow1,
                new() { Id = "Number1", DisplayLabel = "1", SubLabel = "!", Width = 42, PrimaryKey = VirtualKey.Number1 },
                new() { Id = "Number2", DisplayLabel = "2", SubLabel = "@", Width = 42, PrimaryKey = VirtualKey.Number2 },
                new() { Id = "Number3", DisplayLabel = "3", SubLabel = "#", Width = 42, PrimaryKey = VirtualKey.Number3 },
                new() { Id = "Number4", DisplayLabel = "4", SubLabel = "$", Width = 42, PrimaryKey = VirtualKey.Number4 },
                new() { Id = "Number5", DisplayLabel = "5", SubLabel = "%", Width = 42, PrimaryKey = VirtualKey.Number5 },
                new() { Id = "Number6", DisplayLabel = "6", SubLabel = "^", Width = 42, PrimaryKey = VirtualKey.Number6 },
                new() { Id = "Number7", DisplayLabel = "7", SubLabel = "&", Width = 42, PrimaryKey = VirtualKey.Number7 },
                new() { Id = "Number8", DisplayLabel = "8", SubLabel = "*", Width = 42, PrimaryKey = VirtualKey.Number8 },
                new() { Id = "Number9", DisplayLabel = "9", SubLabel = "(", Width = 42, PrimaryKey = VirtualKey.Number9 },
                new() { Id = "Number0", DisplayLabel = "0", SubLabel = ")", Width = 42, PrimaryKey = VirtualKey.Number0 },
                new() { Id = "Minus", DisplayLabel = "-", SubLabel = "_", Width = 42, PrimaryKey = (VirtualKey)189 },
                new() { Id = "Equal", DisplayLabel = "=", SubLabel = "+", Width = 42, PrimaryKey = (VirtualKey)187 },
                new() { Id = "Back", DisplayLabel = "Backspace", Width = 90, PrimaryKey = VirtualKey.Back }
            };
            clusters.MainClusterRows.Add(mainRow1);

            // Row 2: Tab (1.5U = 66px), Q-P, [, ], \ (1.5U = 66px) -> Total = 714px
            var mainRow2 = new List<KeyModel>
            {
                new() { Id = "Tab", DisplayLabel = "Tab", Width = 66, PrimaryKey = VirtualKey.Tab },
                new() { Id = "Q", DisplayLabel = "Q", Width = 42, PrimaryKey = VirtualKey.Q },
                new() { Id = "W", DisplayLabel = "W", Width = 42, PrimaryKey = VirtualKey.W },
                new() { Id = "E", DisplayLabel = "E", Width = 42, PrimaryKey = VirtualKey.E },
                new() { Id = "R", DisplayLabel = "R", Width = 42, PrimaryKey = VirtualKey.R },
                new() { Id = "T", DisplayLabel = "T", Width = 42, PrimaryKey = VirtualKey.T },
                new() { Id = "Y", DisplayLabel = "Y", Width = 42, PrimaryKey = VirtualKey.Y },
                new() { Id = "U", DisplayLabel = "U", Width = 42, PrimaryKey = VirtualKey.U },
                new() { Id = "I", DisplayLabel = "I", Width = 42, PrimaryKey = VirtualKey.I },
                new() { Id = "O", DisplayLabel = "O", Width = 42, PrimaryKey = VirtualKey.O },
                new() { Id = "P", DisplayLabel = "P", Width = 42, PrimaryKey = VirtualKey.P },
                new() { Id = "BracketLeft", DisplayLabel = "[", SubLabel = "{", Width = 42, PrimaryKey = (VirtualKey)219 },
                new() { Id = "BracketRight", DisplayLabel = "]", SubLabel = "}", Width = 42, PrimaryKey = (VirtualKey)221 },
                new() { Id = "Backslash", DisplayLabel = "\\", SubLabel = "|", Width = 66, PrimaryKey = (VirtualKey)220 }
            };
            clusters.MainClusterRows.Add(mainRow2);

            // Row 3: Caps (1.75U = 78px), A-L, ;, ', Enter (2.25U = 102px) -> Total = 714px
            var mainRow3 = new List<KeyModel>
            {
                new() { Id = "CapitalLock", DisplayLabel = "Caps", Width = 78, PrimaryKey = VirtualKey.CapitalLock },
                new() { Id = "A", DisplayLabel = "A", Width = 42, PrimaryKey = VirtualKey.A },
                new() { Id = "S", DisplayLabel = "S", Width = 42, PrimaryKey = VirtualKey.S },
                new() { Id = "D", DisplayLabel = "D", Width = 42, PrimaryKey = VirtualKey.D },
                new() { Id = "F", DisplayLabel = "F", Width = 42, PrimaryKey = VirtualKey.F },
                new() { Id = "G", DisplayLabel = "G", Width = 42, PrimaryKey = VirtualKey.G },
                new() { Id = "H", DisplayLabel = "H", Width = 42, PrimaryKey = VirtualKey.H },
                new() { Id = "J", DisplayLabel = "J", Width = 42, PrimaryKey = VirtualKey.J },
                new() { Id = "K", DisplayLabel = "K", Width = 42, PrimaryKey = VirtualKey.K },
                new() { Id = "L", DisplayLabel = "L", Width = 42, PrimaryKey = VirtualKey.L },
                new() { Id = "Semicolon", DisplayLabel = ";", SubLabel = ":", Width = 42, PrimaryKey = (VirtualKey)186 },
                new() { Id = "Quote", DisplayLabel = "'", SubLabel = "\"", Width = 42, PrimaryKey = (VirtualKey)222 },
                new() { Id = "Enter", DisplayLabel = "Enter", Width = 102, PrimaryKey = VirtualKey.Enter }
            };
            clusters.MainClusterRows.Add(mainRow3);

            // Row 4: LShift (2.25U = 102px), Z-M, ,, ., /, RShift (2.75U = 126px) -> Total = 714px
            var mainRow4 = new List<KeyModel>
            {
                new() { Id = "LeftShift", DisplayLabel = "Shift", Width = 102, PrimaryKey = VirtualKey.LeftShift },
                new() { Id = "Z", DisplayLabel = "Z", Width = 42, PrimaryKey = VirtualKey.Z },
                new() { Id = "X", DisplayLabel = "X", Width = 42, PrimaryKey = VirtualKey.X },
                new() { Id = "C", DisplayLabel = "C", Width = 42, PrimaryKey = VirtualKey.C },
                new() { Id = "V", DisplayLabel = "V", Width = 42, PrimaryKey = VirtualKey.V },
                new() { Id = "B", DisplayLabel = "B", Width = 42, PrimaryKey = VirtualKey.B },
                new() { Id = "N", DisplayLabel = "N", Width = 42, PrimaryKey = VirtualKey.N },
                new() { Id = "M", DisplayLabel = "M", Width = 42, PrimaryKey = VirtualKey.M },
                new() { Id = "Comma", DisplayLabel = ",", SubLabel = "<", Width = 42, PrimaryKey = (VirtualKey)188 },
                new() { Id = "Period", DisplayLabel = ".", SubLabel = ">", Width = 42, PrimaryKey = (VirtualKey)190 },
                new() { Id = "Slash", DisplayLabel = "/", SubLabel = "?", Width = 42, PrimaryKey = (VirtualKey)191 },
                new() { Id = "RightShift", DisplayLabel = "Shift", Width = 126, PrimaryKey = VirtualKey.RightShift }
            };
            clusters.MainClusterRows.Add(mainRow4);

            // Row 5: 3x1.25U (54px), Space (6.25U = 294px), 4x1.25U (54px) -> Total = 714px
            var mainRow5 = new List<KeyModel>
            {
                new() { Id = "LeftControl", DisplayLabel = "Ctrl", Width = 54, PrimaryKey = VirtualKey.LeftControl },
                new() { Id = "LeftWindows", DisplayLabel = "Win", Width = 54, PrimaryKey = VirtualKey.LeftWindows },
                new() { Id = "LeftMenu", DisplayLabel = "Alt", Width = 54, PrimaryKey = VirtualKey.LeftMenu },
                new() { Id = "Space", DisplayLabel = "Space", Width = 294, PrimaryKey = VirtualKey.Space },
                new() { Id = "RightMenu", DisplayLabel = "Alt", Width = 54, PrimaryKey = VirtualKey.RightMenu },
                new() { Id = "RightWindows", DisplayLabel = "Win", Width = 54, PrimaryKey = VirtualKey.RightWindows },
                new() { Id = "Application", DisplayLabel = "Menu", Width = 54, PrimaryKey = VirtualKey.Application },
                new() { Id = "RightControl", DisplayLabel = "Ctrl", Width = 54, PrimaryKey = VirtualKey.RightControl }
            };
            clusters.MainClusterRows.Add(mainRow5);

            // =========================================================================
            // 2. NAVIGATION & ARROW CLUSTER (3.0 Units total row width = 138px)
            // =========================================================================
            if (layoutType != KeyboardLayoutType.Compact61)
            {
                // Row 0: PrtSc, ScrLk, Pause
                var navRow0 = new List<KeyModel>
                {
                    new() { Id = "Snapshot", DisplayLabel = "PrtSc", Width = 42, PrimaryKey = VirtualKey.Snapshot },
                    new() { Id = "Scroll", DisplayLabel = "ScrLk", Width = 42, PrimaryKey = VirtualKey.Scroll },
                    new() { Id = "Pause", DisplayLabel = "Pause", Width = 42, PrimaryKey = VirtualKey.Pause }
                };
                clusters.NavClusterRows.Add(navRow0);

                // Row 1: Insert, Home, PageUp
                var navRow1 = new List<KeyModel>
                {
                    new() { Id = "Insert", DisplayLabel = "Ins", Width = 42, PrimaryKey = VirtualKey.Insert },
                    new() { Id = "Home", DisplayLabel = "Home", Width = 42, PrimaryKey = VirtualKey.Home },
                    new() { Id = "PageUp", DisplayLabel = "PgUp", Width = 42, PrimaryKey = VirtualKey.PageUp }
                };
                clusters.NavClusterRows.Add(navRow1);

                // Row 2: Delete, End, PageDown
                var navRow2 = new List<KeyModel>
                {
                    new() { Id = "Delete", DisplayLabel = "Del", Width = 42, PrimaryKey = VirtualKey.Delete },
                    new() { Id = "End", DisplayLabel = "End", Width = 42, PrimaryKey = VirtualKey.End },
                    new() { Id = "PageDown", DisplayLabel = "PgDn", Width = 42, PrimaryKey = VirtualKey.PageDown }
                };
                clusters.NavClusterRows.Add(navRow2);

                // Row 3: Spacer Row
                var navRow3 = new List<KeyModel>
                {
                    new() { Id = "NavSpacerRow3", Width = 138, Height = 42, IsSpacer = true }
                };
                clusters.NavClusterRows.Add(navRow3);

                // Row 4: Empty (42), Up (42), Empty (42)
                var navRow4 = new List<KeyModel>
                {
                    new() { Id = "NavSpacerUpLeft", Width = 42, Height = 42, IsSpacer = true },
                    new() { Id = "Up", DisplayLabel = "▲", Width = 42, PrimaryKey = VirtualKey.Up },
                    new() { Id = "NavSpacerUpRight", Width = 42, Height = 42, IsSpacer = true }
                };
                clusters.NavClusterRows.Add(navRow4);

                // Row 5: Left, Down, Right
                var navRow5 = new List<KeyModel>
                {
                    new() { Id = "Left", DisplayLabel = "◀", Width = 42, PrimaryKey = VirtualKey.Left },
                    new() { Id = "Down", DisplayLabel = "▼", Width = 42, PrimaryKey = VirtualKey.Down },
                    new() { Id = "Right", DisplayLabel = "▶", Width = 42, PrimaryKey = VirtualKey.Right }
                };
                clusters.NavClusterRows.Add(navRow5);
            }

            // =========================================================================
            // 3. NUMPAD CLUSTER (True 4x5 Grid Layout: authentic 2U tall +, Enter, 2U wide 0)
            // =========================================================================
            if (layoutType == KeyboardLayoutType.Full104)
            {
                clusters.NumpadGridKeys = new List<KeyModel>
                {
                    // Row 0: Top spacer row to align with F-Row
                    new() { Id = "NumTopSpacer", Width = 186, Height = 42, Row = 0, Column = 0, ColumnSpan = 4, IsSpacer = true },

                    // Row 1: NumLock, /, *, -
                    new() { Id = "NumberKeyLock", DisplayLabel = "Num", Width = 42, Height = 42, Row = 1, Column = 0, PrimaryKey = VirtualKey.NumberKeyLock },
                    new() { Id = "Divide", DisplayLabel = "/", Width = 42, Height = 42, Row = 1, Column = 1, PrimaryKey = VirtualKey.Divide },
                    new() { Id = "Multiply", DisplayLabel = "*", Width = 42, Height = 42, Row = 1, Column = 2, PrimaryKey = VirtualKey.Multiply },
                    new() { Id = "Subtract", DisplayLabel = "-", Width = 42, Height = 42, Row = 1, Column = 3, PrimaryKey = VirtualKey.Subtract },

                    // Row 2: 7, 8, 9, + (RowSpan = 2, Height = 88px)
                    new() { Id = "NumberPad7", DisplayLabel = "7", Width = 42, Height = 42, Row = 2, Column = 0, PrimaryKey = VirtualKey.NumberPad7 },
                    new() { Id = "NumberPad8", DisplayLabel = "8", Width = 42, Height = 42, Row = 2, Column = 1, PrimaryKey = VirtualKey.NumberPad8 },
                    new() { Id = "NumberPad9", DisplayLabel = "9", Width = 42, Height = 42, Row = 2, Column = 2, PrimaryKey = VirtualKey.NumberPad9 },
                    new() { Id = "Add", DisplayLabel = "+", Width = 42, Height = 88, Row = 2, Column = 3, RowSpan = 2, PrimaryKey = VirtualKey.Add },

                    // Row 3: 4, 5, 6 (+ spans here)
                    new() { Id = "NumberPad4", DisplayLabel = "4", Width = 42, Height = 42, Row = 3, Column = 0, PrimaryKey = VirtualKey.NumberPad4 },
                    new() { Id = "NumberPad5", DisplayLabel = "5", Width = 42, Height = 42, Row = 3, Column = 1, PrimaryKey = VirtualKey.NumberPad5 },
                    new() { Id = "NumberPad6", DisplayLabel = "6", Width = 42, Height = 42, Row = 3, Column = 2, PrimaryKey = VirtualKey.NumberPad6 },

                    // Row 4: 1, 2, 3, Enter (RowSpan = 2, Height = 88px)
                    new() { Id = "NumberPad1", DisplayLabel = "1", Width = 42, Height = 42, Row = 4, Column = 0, PrimaryKey = VirtualKey.NumberPad1 },
                    new() { Id = "NumberPad2", DisplayLabel = "2", Width = 42, Height = 42, Row = 4, Column = 1, PrimaryKey = VirtualKey.NumberPad2 },
                    new() { Id = "NumberPad3", DisplayLabel = "3", Width = 42, Height = 42, Row = 4, Column = 2, PrimaryKey = VirtualKey.NumberPad3 },
                    new() { Id = "NumEnter", DisplayLabel = "Enter", Width = 42, Height = 88, Row = 4, Column = 3, RowSpan = 2, PrimaryKey = (VirtualKey)13, ScanCode = 0xE01C },

                    // Row 5: 0 (ColumnSpan = 2, Width = 90px), . (Enter spans here)
                    new() { Id = "NumberPad0", DisplayLabel = "0", Width = 90, Height = 42, Row = 5, Column = 0, ColumnSpan = 2, PrimaryKey = VirtualKey.NumberPad0 },
                    new() { Id = "Decimal", DisplayLabel = ".", Width = 42, Height = 42, Row = 5, Column = 2, PrimaryKey = VirtualKey.Decimal }
                };
            }

            return clusters;
        }
    }
}
