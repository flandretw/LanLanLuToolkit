using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class InputTesterPage : Page
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool MessageBeep(uint uType);

        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;

        // Visual Key Mapping dictionaries for O(1) fast lookup
        private readonly Dictionary<string, Border> _keyVisualMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, KeyModel> _keyModelMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _testedKeys = new(StringComparer.OrdinalIgnoreCase);

        private int _maxRollover = 0;
        private int _totalPresses = 0;
        private long _lastKeyTimestamp = 0;
        private string _lastPressedKeyId = string.Empty;
        private KeyboardLayoutType _currentLayoutType = KeyboardLayoutType.Full104;
        private int _currentLayoutTotalKeys = 104;

        // Mouse Diagnostic States
        private int _mouseLeftCount = 0;
        private int _mouseRightCount = 0;
        private int _mouseMiddleCount = 0;
        private int _mouseSide1Count = 0;
        private int _mouseSide2Count = 0;
        private int _wheelUpCount = 0;
        private int _wheelDownCount = 0;
        private int _normalClicks = 0;
        private int _chatterClicks = 0;
        private long _lastMouseClickTimestamp = 0;
        private int _lastWheelDirection = 0; // 1 = Up, -1 = Down
        private int _consecutiveWheelSameDir = 0;

        // Polling Rate Tracking
        private int _pollingEventCount = 0;
        private long _pollingWindowStartTimestamp = 0;
        private double _peakHz = 0;
        private readonly List<double> _hzHistory = new();
        private readonly DispatcherTimer _hzUpdateTimer = new();

        // Canvas Trajectory Drawing
        private bool _isDrawing = false;
        private Point _lastPoint;

        private DispatcherTimer? _wheelVisualResetTimer;

        private readonly PointerEventHandler _pointerPressedHandler;
        private readonly PointerEventHandler _pointerReleasedHandler;
        private readonly PointerEventHandler _pointerMovedHandler;
        private readonly PointerEventHandler _pointerCanceledHandler;
        private readonly PointerEventHandler _pointerCaptureLostHandler;
        private bool _isLoaded = false;

        public InputTesterPage()
        {
            this.InitializeComponent();
            BuildKeyboardLayout();

            _hzUpdateTimer.Interval = TimeSpan.FromMilliseconds(200);
            _hzUpdateTimer.Tick += HzUpdateTimer_Tick;

            _wheelVisualResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _wheelVisualResetTimer.Tick += (s, e) =>
            {
                _wheelVisualResetTimer.Stop();
                WheelUpArrow.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
                WheelDownArrow.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
            };

            _pointerPressedHandler = new PointerEventHandler(OnGlobalPointerPressed);
            _pointerReleasedHandler = new PointerEventHandler(OnGlobalPointerReleased);
            _pointerMovedHandler = new PointerEventHandler(OnGlobalPointerMoved);
            _pointerCanceledHandler = new PointerEventHandler(OnGlobalPointerCanceled);
            _pointerCaptureLostHandler = new PointerEventHandler(OnGlobalPointerCaptureLost);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            _hzUpdateTimer.Start();
            KeyboardFocusArea?.Focus(FocusState.Programmatic);
            UpdateFocusVisual(true);
            ResetMouseVisualsAndStats();

            this.AddHandler(UIElement.PointerPressedEvent, _pointerPressedHandler, true);
            this.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, true);
            this.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, true);
            this.AddHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler, true);
            this.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, true);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _hzUpdateTimer.Stop();
            _wheelVisualResetTimer?.Stop();

            this.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressedHandler);
            this.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
            this.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
            this.RemoveHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler);
            this.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
        }

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (ModeRadioButtons.SelectedIndex == 0)
            {
                if (!ReferenceEquals(FocusManager.GetFocusedElement(this.XamlRoot), KeyboardFocusArea))
                {
                    KeyboardFocusArea.Focus(FocusState.Programmatic);
                }
                KeyboardFocusArea_KeyDown(sender, e);
            }
        }

        private void Page_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (ModeRadioButtons.SelectedIndex == 0)
            {
                KeyboardFocusArea_KeyUp(sender, e);
            }
        }

        private static Brush GetThemeBrush(string resourceKey)
        {
            if (Application.Current.Resources.TryGetValue(resourceKey, out var res) && res is Brush brush)
            {
                return brush;
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        #region Keyboard Visual Layout & Events

        private void BuildKeyboardLayout()
        {
            if (MainClusterPanel == null || NavClusterPanel == null || NumpadClusterGrid == null) return;

            MainClusterPanel.Children.Clear();
            NavClusterPanel.Children.Clear();
            NumpadClusterGrid.Children.Clear();
            _keyVisualMap.Clear();
            _keyModelMap.Clear();

            var clusters = KeyboardLayoutProvider.GenerateClusters(_currentLayoutType);

            PopulateCluster(MainClusterPanel, clusters.MainClusterRows);

            if (_currentLayoutType != KeyboardLayoutType.Compact61)
            {
                NavClusterPanel.Visibility = Visibility.Visible;
                PopulateCluster(NavClusterPanel, clusters.NavClusterRows);
            }
            else
            {
                NavClusterPanel.Visibility = Visibility.Collapsed;
            }

            if (_currentLayoutType == KeyboardLayoutType.Full104)
            {
                NumpadClusterGrid.Visibility = Visibility.Visible;
                foreach (var key in clusters.NumpadGridKeys)
                {
                    var keyElement = CreateKeyElement(key);
                    Grid.SetRow(keyElement, key.Row);
                    Grid.SetColumn(keyElement, key.Column);
                    if (key.RowSpan > 1) Grid.SetRowSpan(keyElement, key.RowSpan);
                    if (key.ColumnSpan > 1) Grid.SetColumnSpan(keyElement, key.ColumnSpan);

                    NumpadClusterGrid.Children.Add(keyElement);

                    if (!key.IsSpacer)
                    {
                        _keyVisualMap[key.Id] = (Border)keyElement;
                        _keyModelMap[key.Id] = key;
                    }
                }
            }
            else
            {
                NumpadClusterGrid.Visibility = Visibility.Collapsed;
            }

            _currentLayoutTotalKeys = _currentLayoutType switch
            {
                KeyboardLayoutType.Full104 => 104,
                KeyboardLayoutType.Tkl87 => 87,
                KeyboardLayoutType.Compact61 => 61,
                _ => 104
            };

            if (KeyboardViewbox != null)
            {
                KeyboardViewbox.MaxHeight = 310;
            }

            foreach (var testedKeyId in _testedKeys)
            {
                if (_keyVisualMap.ContainsKey(testedKeyId))
                {
                    ApplyKeyVisualState(testedKeyId, KeyVisualState.Tested);
                }
            }

            UpdateTestedKeysStat();
        }

        private void UpdateTestedKeysStat()
        {
            int count = 0;
            foreach (var k in _testedKeys)
            {
                if (_keyVisualMap.ContainsKey(k))
                {
                    count++;
                }
            }
            if (TestedKeysCountText != null)
            {
                TestedKeysCountText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Key_TestedCountFormat"), count, _currentLayoutTotalKeys);
            }
        }

        private void KeyboardLayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || KeyboardLayoutComboBox == null || KeyboardLayoutTitleText == null || MainClusterPanel == null) return;

            _currentLayoutType = (KeyboardLayoutType)KeyboardLayoutComboBox.SelectedIndex;

            KeyboardLayoutTitleText.Text = _currentLayoutType switch
            {
                KeyboardLayoutType.Full104 => LocalizationHelper.GetString("InputTesterPage_LayoutTitle_100/Text"),
                KeyboardLayoutType.Tkl87 => LocalizationHelper.GetString("InputTesterPage_LayoutTitle_80/Text"),
                KeyboardLayoutType.Compact61 => LocalizationHelper.GetString("InputTesterPage_LayoutTitle_60/Text"),
                _ => LocalizationHelper.GetString("InputTesterPage_LayoutTitle_100/Text")
            };

            BuildKeyboardLayout();

            KeyboardFocusArea?.Focus(FocusState.Programmatic);
            UpdateFocusVisual(true);
        }

        private void PopulateCluster(StackPanel clusterPanel, List<List<KeyModel>> rows)
        {
            foreach (var row in rows)
            {
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    UseLayoutRounding = false
                };

                foreach (var key in row)
                {
                    var keyElement = CreateKeyElement(key);
                    rowPanel.Children.Add(keyElement);

                    if (!key.IsSpacer)
                    {
                        _keyVisualMap[key.Id] = (Border)keyElement;
                        _keyModelMap[key.Id] = key;
                    }
                }

                clusterPanel.Children.Add(rowPanel);
            }
        }

        private FrameworkElement CreateKeyElement(KeyModel key)
        {
            if (key.IsSpacer)
            {
                return new Border
                {
                    Width = key.Width,
                    Height = key.Height,
                    UseLayoutRounding = false,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0)
                };
            }

            var border = new Border
            {
                Width = key.Width,
                Height = key.Height,
                Margin = new Thickness(key.LeftMargin, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Tag = key.Id,
                UseLayoutRounding = false,
                Background = GetThemeBrush("ControlFillColorDefaultBrush"),
                BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush")
            };

            border.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                if (s is Border b && b.Tag is string clickedKeyId && !string.IsNullOrEmpty(clickedKeyId))
                {
                    _totalPresses++;
                    _testedKeys.Add(clickedKeyId);
                    PlayKeySound();
                    ApplyKeyVisualState(clickedKeyId, KeyVisualState.Pressed);
                    _ = DispatcherQueue.TryEnqueue(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(150);
                        ApplyKeyVisualState(clickedKeyId, KeyVisualState.Tested);
                    });
                    LastKeyText.Text = _keyModelMap.TryGetValue(clickedKeyId, out var model) ? model.DisplayLabel : clickedKeyId;
                    TotalPressesText.Text = _totalPresses.ToString();
                    UpdateTestedKeysStat();
                }
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 0
            };

            if (!string.IsNullOrEmpty(key.SubLabel))
            {
                var subText = new TextBlock
                {
                    Text = key.SubLabel,
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
                };
                stack.Children.Add(subText);
            }

            var mainText = new TextBlock
            {
                Text = key.DisplayLabel,
                FontSize = key.DisplayLabel.Length > 3 ? 10 : 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeBrush("TextFillColorPrimaryBrush")
            };
            stack.Children.Add(mainText);

            border.Child = stack;
            return border;
        }

        private void PlayKeySound()
        {
            if (!SoundToggle.IsOn) return;
            try
            {
                MessageBeep(0xFFFFFFFF);
            }
            catch
            {
                try { Console.Beep(800, 20); } catch { }
            }
        }

        private void KeyboardFocusArea_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            e.Handled = true;
            var keyId = ResolveKeyId(e.Key, e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey);

            if (string.IsNullOrEmpty(keyId)) return;

            long currentTimestamp = Stopwatch.GetTimestamp();
            double intervalMs = 0;
            if (_lastKeyTimestamp > 0)
            {
                intervalMs = (currentTimestamp - _lastKeyTimestamp) * 1000.0 / Stopwatch.Frequency;
            }

            bool isChatter = false;
            if (string.Equals(_lastPressedKeyId, keyId, StringComparison.OrdinalIgnoreCase) && intervalMs > 0 && intervalMs < 40)
            {
                isChatter = true;
            }

            _lastKeyTimestamp = currentTimestamp;
            _lastPressedKeyId = keyId;
            _totalPresses++;

            _activeKeys.Add(keyId);
            _testedKeys.Add(keyId);

            if (_activeKeys.Count > _maxRollover)
            {
                _maxRollover = _activeKeys.Count;
            }

            // Play crisp audible sound
            PlayKeySound();

            // Update Key Visual State
            ApplyKeyVisualState(keyId, isChatter ? KeyVisualState.Warning : KeyVisualState.Pressed);

            // Special auto-reset for Snapshot (PrintScreen) in case Windows OS drops the KeyUp event
            if (keyId == "Snapshot")
            {
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(150);
                    _activeKeys.Remove("Snapshot");
                    ApplyKeyVisualState("Snapshot", KeyVisualState.Tested);
                    CurrentDownText.Text = _activeKeys.Count.ToString();
                    ActiveKeyListText.Text = _activeKeys.Count > 0 ? string.Join(", ", _activeKeys) : LocalizationHelper.GetString("InputTesterPage_Key_None");
                });
            }

            // Special auto-reset for Windows keys (since Start menu causes focus loss)
            if (keyId == "LeftWindows" || keyId == "RightWindows")
            {
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(200);
                    if (_activeKeys.Contains(keyId))
                    {
                        _activeKeys.Remove(keyId);
                        ApplyKeyVisualState(keyId, KeyVisualState.Tested);
                        CurrentDownText.Text = _activeKeys.Count.ToString();
                        ActiveKeyListText.Text = _activeKeys.Count > 0 ? string.Join(", ", _activeKeys) : LocalizationHelper.GetString("InputTesterPage_Key_None");
                    }
                });
            }

            // Update Stat Cards
            LastKeyText.Text = _keyModelMap.TryGetValue(keyId, out var model) ? model.DisplayLabel : e.Key.ToString();
            LastKeyCodeText.Text = $"VK: {(int)e.Key} (0x{(int)e.Key:X2}) | Scan: 0x{e.KeyStatus.ScanCode:X2}";
            CurrentDownText.Text = _activeKeys.Count.ToString();
            ActiveKeyListText.Text = string.Join(", ", _activeKeys);
            MaxRolloverText.Text = $"{_maxRollover} Keys";
            TotalPressesText.Text = _totalPresses.ToString();
            UpdateTestedKeysStat();

            if (intervalMs > 0)
            {
                KeyIntervalText.Text = $"{intervalMs:F1} ms";
                if (isChatter)
                {
                    KeyChatterStatusText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Key_ChatterWarning"), $"{intervalMs:F0}");
                    KeyChatterStatusText.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                }
                else
                {
                    KeyChatterStatusText.Text = "OK";
                    KeyChatterStatusText.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
                }
            }
        }

        private void KeyboardFocusArea_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            e.Handled = true;
            var keyId = ResolveKeyId(e.Key, e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey);

            if (string.IsNullOrEmpty(keyId)) return;

            // If Windows sent KeyUp for Snapshot without KeyDown (e.g. Snipping tool capture)
            if (keyId == "Snapshot" && !_activeKeys.Contains("Snapshot"))
            {
                _totalPresses++;
                _testedKeys.Add("Snapshot");
                PlayKeySound();
                ApplyKeyVisualState("Snapshot", KeyVisualState.Pressed);
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(150);
                    ApplyKeyVisualState("Snapshot", KeyVisualState.Tested);
                });
                LastKeyText.Text = "PrtSc";
                LastKeyCodeText.Text = "VK: 44 (0x2C) | Scan: 0x37";
                TotalPressesText.Text = _totalPresses.ToString();
                UpdateTestedKeysStat();
                return;
            }

            _activeKeys.Remove(keyId);
            ApplyKeyVisualState(keyId, KeyVisualState.Tested);

            CurrentDownText.Text = _activeKeys.Count.ToString();
            ActiveKeyListText.Text = _activeKeys.Count > 0 ? string.Join(", ", _activeKeys) : LocalizationHelper.GetString("InputTesterPage_Key_None");
        }

        private void KeyboardFocusArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            KeyboardFocusArea.Focus(FocusState.Programmatic);
            UpdateFocusVisual(true);
        }

        private void KeyboardFocusArea_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdateFocusVisual(true);
        }

        private void KeyboardFocusArea_LostFocus(object sender, RoutedEventArgs e)
        {
            foreach (var keyId in _activeKeys)
            {
                ApplyKeyVisualState(keyId, KeyVisualState.Tested);
            }
            _activeKeys.Clear();
            CurrentDownText.Text = "0";
            ActiveKeyListText.Text = LocalizationHelper.GetString("InputTesterPage_Key_None");
            UpdateFocusVisual(false);
        }

        private void UpdateFocusVisual(bool isFocused)
        {
            if (KeyboardFocusArea == null || FocusStatusBadge == null || FocusStatusText == null) return;

            if (isFocused)
            {
                KeyboardFocusArea.BorderBrush = GetThemeBrush("AccentFillColorDefaultBrush");
                KeyboardFocusArea.BorderThickness = new Thickness(1.5);
                FocusStatusBadge.Background = new SolidColorBrush(Color.FromArgb(35, 46, 125, 50));
                FocusStatusBadge.BorderBrush = GetThemeBrush("SystemFillColorSuccessBrush");
                if (FocusStatusIcon != null)
                {
                    FocusStatusIcon.Glyph = "\uEA3B";
                    FocusStatusIcon.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
                }
                FocusStatusText.Text = LocalizationHelper.GetString("InputTesterPage_FocusStatus_Focused/Text");
                FocusStatusText.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
            }
            else
            {
                KeyboardFocusArea.BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush");
                KeyboardFocusArea.BorderThickness = new Thickness(1);
                FocusStatusBadge.Background = GetThemeBrush("ControlFillColorDefaultBrush");
                FocusStatusBadge.BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush");
                if (FocusStatusIcon != null)
                {
                    FocusStatusIcon.Glyph = "\uEA3A";
                    FocusStatusIcon.Foreground = GetThemeBrush("TextFillColorSecondaryBrush");
                }
                FocusStatusText.Text = LocalizationHelper.GetString("InputTesterPage_FocusStatus_Unfocused/Text");
                FocusStatusText.Foreground = GetThemeBrush("TextFillColorSecondaryBrush");
            }
        }

        private string ResolveKeyId(VirtualKey key, uint scanCode, bool isExtended)
        {
            // 1. Specific function / control keys that must never be overridden by scan code
            if (key == VirtualKey.Pause || (int)key == 19)
            {
                return "Pause";
            }
            if (key == VirtualKey.Snapshot || (int)key == 44)
            {
                return "Snapshot";
            }
            if (key == VirtualKey.Scroll || (int)key == 145)
            {
                return "Scroll";
            }
            if (key == VirtualKey.NumberKeyLock || (int)key == 144)
            {
                return "NumberKeyLock";
            }

            // 2. Modifier keys distinction (Left vs Right via ScanCode & Extended)
            if (key == VirtualKey.Shift || key == VirtualKey.LeftShift || key == VirtualKey.RightShift)
            {
                if (scanCode == 0x36) return "RightShift";
                if (scanCode == 0x2A) return "LeftShift";
                return (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0 ? "RightShift" : "LeftShift";
            }
            if (key == VirtualKey.Control || key == VirtualKey.LeftControl || key == VirtualKey.RightControl)
            {
                if (isExtended || (scanCode == 0x1D && isExtended)) return "RightControl";
                return "LeftControl";
            }
            if (key == VirtualKey.Menu || key == VirtualKey.LeftMenu || key == VirtualKey.RightMenu)
            {
                if (isExtended || (scanCode == 0x38 && isExtended)) return "RightMenu";
                return "LeftMenu";
            }
            if (key == VirtualKey.LeftWindows || key == (VirtualKey)91 || (scanCode == 0x5B && isExtended))
            {
                return "LeftWindows";
            }
            if (key == VirtualKey.RightWindows || key == (VirtualKey)92 || (scanCode == 0x5C && isExtended))
            {
                return "RightWindows";
            }
            if (key == VirtualKey.Application || key == (VirtualKey)93 || (scanCode == 0x5D && isExtended))
            {
                return "Application";
            }
            if (key == VirtualKey.Enter)
            {
                return isExtended ? "NumEnter" : "Enter";
            }

            // 3. ScanCode based resolution for Numpad keys (handles both NumLock ON & OFF)
            if (!isExtended)
            {
                switch (scanCode)
                {
                    case 0x45: return "NumberKeyLock";
                    case 0x37: return "Multiply";
                    case 0x4A: return "Subtract";
                    case 0x4E: return "Add";
                    case 0x47: return "NumberPad7";
                    case 0x48: return "NumberPad8";
                    case 0x49: return "NumberPad9";
                    case 0x4B: return "NumberPad4";
                    case 0x4C: return "NumberPad5";
                    case 0x4D: return "NumberPad6";
                    case 0x4F: return "NumberPad1";
                    case 0x50: return "NumberPad2";
                    case 0x51: return "NumberPad3";
                    case 0x52: return "NumberPad0";
                    case 0x53: return "Decimal";
                }
            }
            else
            {
                if (scanCode == 0x35) return "Divide";
                if (scanCode == 0x1C) return "NumEnter";
            }

            // 4. Direct Key name mapping
            string keyStr = key.ToString();
            if (_keyVisualMap.ContainsKey(keyStr)) return keyStr;

            // 5. OEM punctuation mapping
            int vkInt = (int)key;
            return vkInt switch
            {
                192 => "Backquote",
                189 => "Minus",
                187 => "Equal",
                219 => "BracketLeft",
                221 => "BracketRight",
                220 => "Backslash",
                186 => "Semicolon",
                222 => "Quote",
                188 => "Comma",
                190 => "Period",
                191 => "Slash",
                _ => keyStr
            };
        }

        private void ApplyKeyVisualState(string keyId, KeyVisualState state)
        {
            if (!_keyVisualMap.TryGetValue(keyId, out var border)) return;

            switch (state)
            {
                case KeyVisualState.Pressed:
                    border.Background = GetThemeBrush("AccentFillColorDefaultBrush");
                    border.BorderBrush = GetThemeBrush("AccentFillColorDefaultBrush");
                    SetTextColors(border, Colors.White);
                    break;

                case KeyVisualState.Tested:
                    border.Background = new SolidColorBrush(Color.FromArgb(255, 31, 78, 91)); // Subtle active teal
                    border.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 42, 157, 143));
                    SetTextColors(border, Colors.White);
                    break;

                case KeyVisualState.Warning:
                    border.Background = GetThemeBrush("SystemFillColorCriticalBrush");
                    border.BorderBrush = GetThemeBrush("SystemFillColorCriticalBrush");
                    SetTextColors(border, Colors.White);
                    break;

                case KeyVisualState.Default:
                default:
                    border.Background = GetThemeBrush("ControlFillColorDefaultBrush");
                    border.BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush");
                    ResetTextColors(border);
                    break;
            }
        }

        private static void SetTextColors(Border border, Color color)
        {
            if (border.Child is StackPanel stack)
            {
                foreach (var child in stack.Children)
                {
                    if (child is TextBlock tb)
                    {
                        tb.Foreground = new SolidColorBrush(color);
                    }
                }
            }
        }

        private static void ResetTextColors(Border border)
        {
            if (border.Child is StackPanel stack)
            {
                foreach (var child in stack.Children)
                {
                    if (child is TextBlock tb)
                    {
                        tb.Foreground = GetThemeBrush("TextFillColorPrimaryBrush");
                    }
                }
            }
        }

        #endregion

        #region Mouse Visualizer & Diagnostics

        private void HandleMouseButtonPress(PointerPointProperties props)
        {
            var updateKind = props.PointerUpdateKind;
            string lastBtnName = "-";

            if (updateKind == PointerUpdateKind.LeftButtonPressed || (updateKind == PointerUpdateKind.Other && props.IsLeftButtonPressed))
            {
                _mouseLeftCount++;
                MouseLeftCountText.Text = _mouseLeftCount.ToString();
                HighlightBorder(MouseLeftBtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_LeftButton");
            }
            else if (updateKind == PointerUpdateKind.RightButtonPressed || (updateKind == PointerUpdateKind.Other && props.IsRightButtonPressed))
            {
                _mouseRightCount++;
                MouseRightCountText.Text = _mouseRightCount.ToString();
                HighlightBorder(MouseRightBtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_RightButton");
            }
            else if (updateKind == PointerUpdateKind.MiddleButtonPressed || (updateKind == PointerUpdateKind.Other && props.IsMiddleButtonPressed))
            {
                _mouseMiddleCount++;
                HighlightBorder(MouseMiddleBtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_MiddleButton");
            }
            else if (updateKind == PointerUpdateKind.XButton1Pressed || (updateKind == PointerUpdateKind.Other && props.IsXButton1Pressed))
            {
                _mouseSide1Count++;
                Side1CountText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ClickCountUnit"), _mouseSide1Count);
                HighlightBorder(MouseSide1BtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_Side1Button");
            }
            else if (updateKind == PointerUpdateKind.XButton2Pressed || (updateKind == PointerUpdateKind.Other && props.IsXButton2Pressed))
            {
                _mouseSide2Count++;
                Side2CountText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ClickCountUnit"), _mouseSide2Count);
                HighlightBorder(MouseSide2BtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_Side2Button");
            }

            PlayKeySound();

            int totalClicks = _mouseLeftCount + _mouseRightCount + _mouseMiddleCount + _mouseSide1Count + _mouseSide2Count;

            // Update Mouse Summary Cards
            if (LastMouseBtnText != null) LastMouseBtnText.Text = lastBtnName;
            if (LastMouseActionText != null) LastMouseActionText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_CumulativeClicks"), totalClicks);
            if (MouseTotalClicksCountText != null) MouseTotalClicksCountText.Text = totalClicks.ToString();
            if (MouseClicksBreakdownText != null) MouseClicksBreakdownText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ClicksBreakdown"), _mouseLeftCount, _mouseRightCount, _mouseMiddleCount);
        }

        private void HandleMouseButtonRelease(PointerPointProperties props)
        {
            var updateKind = props.PointerUpdateKind;

            if (updateKind == PointerUpdateKind.LeftButtonReleased || !props.IsLeftButtonPressed)
            {
                HighlightBorder(MouseLeftBtnVisual, false);
            }
            if (updateKind == PointerUpdateKind.RightButtonReleased || !props.IsRightButtonPressed)
            {
                HighlightBorder(MouseRightBtnVisual, false);
            }
            if (updateKind == PointerUpdateKind.MiddleButtonReleased || !props.IsMiddleButtonPressed)
            {
                HighlightBorder(MouseMiddleBtnVisual, false);
            }
            if (updateKind == PointerUpdateKind.XButton1Released || !props.IsXButton1Pressed)
            {
                HighlightBorder(MouseSide1BtnVisual, false);
            }
            if (updateKind == PointerUpdateKind.XButton2Released || !props.IsXButton2Pressed)
            {
                HighlightBorder(MouseSide2BtnVisual, false);
            }
        }

        private void OnGlobalPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (ModeRadioButtons == null || ModeRadioButtons.SelectedIndex != 1) return;

            if (e.OriginalSource is DependencyObject dep)
            {
                if (IsDescendantOf<Button>(dep) || IsDescendantOf<Slider>(dep) || IsDescendantOf<ToggleSwitch>(dep) || IsDescendantOf<RadioButton>(dep))
                {
                    return;
                }
            }

            var props = e.GetCurrentPoint(this).Properties;
            HandleMouseButtonPress(props);
        }

        private void OnGlobalPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (ModeRadioButtons == null || ModeRadioButtons.SelectedIndex != 1) return;

            var props = e.GetCurrentPoint(this).Properties;
            HandleMouseButtonRelease(props);
        }

        private void OnGlobalPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (ModeRadioButtons == null || ModeRadioButtons.SelectedIndex != 1) return;

            var props = e.GetCurrentPoint(this).Properties;
            if (!props.IsLeftButtonPressed) HighlightBorder(MouseLeftBtnVisual, false);
            if (!props.IsRightButtonPressed) HighlightBorder(MouseRightBtnVisual, false);
            if (!props.IsMiddleButtonPressed) HighlightBorder(MouseMiddleBtnVisual, false);
            if (!props.IsXButton1Pressed) HighlightBorder(MouseSide1BtnVisual, false);
            if (!props.IsXButton2Pressed) HighlightBorder(MouseSide2BtnVisual, false);
        }

        private void OnGlobalPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            ReleaseAllMouseButtonVisuals();
        }

        private void OnGlobalPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            ReleaseAllMouseButtonVisuals();
        }

        private void ReleaseAllMouseButtonVisuals()
        {
            HighlightBorder(MouseLeftBtnVisual, false);
            HighlightBorder(MouseRightBtnVisual, false);
            HighlightBorder(MouseMiddleBtnVisual, false);
            HighlightBorder(MouseSide1BtnVisual, false);
            HighlightBorder(MouseSide2BtnVisual, false);
        }

        private static bool IsDescendantOf<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null)
            {
                if (element is T) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void MouseBtnVisual_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Global handler takes care of press
        }

        private void MouseBtnVisual_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            // Global handler takes care of release
        }

        private void ChatterClickPad_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            long now = Stopwatch.GetTimestamp();
            double intervalMs = 0;

            if (_lastMouseClickTimestamp > 0)
            {
                intervalMs = (now - _lastMouseClickTimestamp) * 1000.0 / Stopwatch.Frequency;
            }
            _lastMouseClickTimestamp = now;

            double threshold = ChatterThresholdSlider.Value;
            bool isChatter = intervalMs > 0 && intervalMs < threshold;

            if (isChatter)
            {
                _chatterClicks++;
                ChatterClicksText.Text = _chatterClicks.ToString();
                ChatterClicksText.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                ChatterAlertBanner.Visibility = Visibility.Visible;
                if (ChatterStatusSummaryText != null)
                {
                    ChatterStatusSummaryText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterStatus_Warning");
                    ChatterStatusSummaryText.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                }
            }
            else
            {
                _normalClicks++;
                NormalClicksText.Text = _normalClicks.ToString();
                ChatterAlertBanner.Visibility = Visibility.Collapsed;
                if (_chatterClicks == 0 && ChatterStatusSummaryText != null)
                {
                    ChatterStatusSummaryText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterStatus_OK");
                    ChatterStatusSummaryText.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
                }
            }

            if (ChatterCountsText != null)
            {
                ChatterCountsText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterCounts"), _normalClicks, _chatterClicks);
            }

            LastClickIntervalText.Text = intervalMs > 0 ? string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_LastIntervalFormat"), $"{intervalMs:F1}") : LocalizationHelper.GetString("InputTesterPage_Mouse_LastIntervalNone");
            if (LastMouseActionText != null && intervalMs > 0)
            {
                LastMouseActionText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_IntervalAction"), $"{intervalMs:F1}");
            }
            ChatterClickPad.Background = GetThemeBrush("AccentFillColorDefaultBrush");
        }

        private void ChatterClickPad_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            ChatterClickPad.Background = GetThemeBrush("ControlAltFillColorSecondaryBrush");
        }

        private void MouseVisualCard_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
            int delta = e.GetCurrentPoint(sender as UIElement).Properties.MouseWheelDelta;
            HandleWheelScroll(delta);
        }

        private void MouseSection_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            MouseVisualCard_PointerWheelChanged(sender, e);
        }

        private void MouseMiddleBtnVisual_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            MouseVisualCard_PointerWheelChanged(sender, e);
        }

        private void HandleWheelScroll(int delta)
        {
            int currentDir = delta > 0 ? 1 : -1;

            if (currentDir > 0)
            {
                _wheelUpCount++;
                WheelUpArrow.Foreground = GetThemeBrush("AccentFillColorDefaultBrush");
                WheelDownArrow.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
            }
            else
            {
                _wheelDownCount++;
                WheelDownArrow.Foreground = GetThemeBrush("AccentFillColorDefaultBrush");
                WheelUpArrow.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
            }

            _wheelVisualResetTimer?.Stop();
            _wheelVisualResetTimer?.Start();

            // Detect Wheel Jitter (Inversion during continuous scrolling)
            if (_lastWheelDirection != 0 && currentDir != _lastWheelDirection && _consecutiveWheelSameDir >= 3)
            {
                WheelStatusIcon.Glyph = "\uE7BA";
                WheelStatusIcon.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                WheelStatusText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_WheelStatus_Jitter");
                WheelStatusText.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                _consecutiveWheelSameDir = 1;
            }
            else
            {
                if (currentDir == _lastWheelDirection)
                {
                    _consecutiveWheelSameDir++;
                    if (_consecutiveWheelSameDir > 5)
                    {
                        WheelStatusIcon.Glyph = "\uE73E";
                        WheelStatusIcon.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
                        WheelStatusText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_WheelStatus_OK");
                        WheelStatusText.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
                    }
                }
                else
                {
                    _consecutiveWheelSameDir = 1;
                }
            }

            _lastWheelDirection = currentDir;
            WheelDeltaText.Text = $"{_wheelUpCount} / {_wheelDownCount}";

            if (WheelStepsSummaryText != null)
            {
                WheelStepsSummaryText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_StepsUnit"), _wheelUpCount + _wheelDownCount);
            }
            if (WheelDeltaBreakdownText != null)
            {
                string status = _consecutiveWheelSameDir > 5 || _lastWheelDirection == 0 ? LocalizationHelper.GetString("InputTesterPage_Mouse_WheelStatus_OK") : LocalizationHelper.GetString("InputTesterPage_Mouse_WheelStatus_Jitter");
                WheelDeltaBreakdownText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_WheelBreakdown"), _wheelUpCount, _wheelDownCount, status);
            }
            if (LastMouseBtnText != null)
            {
                LastMouseBtnText.Text = currentDir > 0 ? LocalizationHelper.GetString("InputTesterPage_Mouse_WheelUp") : LocalizationHelper.GetString("InputTesterPage_Mouse_WheelDown");
            }
            if (LastMouseActionText != null)
            {
                LastMouseActionText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_WheelScrollAction"), delta);
            }
        }

        private void HzUpdateTimer_Tick(object? sender, object e)
        {
            if (_pollingWindowStartTimestamp == 0) return;

            long now = Stopwatch.GetTimestamp();
            double elapsedSec = (now - _pollingWindowStartTimestamp) / (double)Stopwatch.Frequency;

            if (elapsedSec >= 0.2)
            {
                double hz = _pollingEventCount / elapsedSec;
                _pollingEventCount = 0;
                _pollingWindowStartTimestamp = now;

                if (hz > 0)
                {
                    CurrentHzText.Text = $"{hz:F0} Hz";
                    if (MouseCurrentHzSummaryText != null) MouseCurrentHzSummaryText.Text = $"{hz:F0} Hz";

                    if (hz > _peakHz)
                    {
                        _peakHz = hz;
                        PeakHzText.Text = $"{_peakHz:F0} Hz";
                    }

                    _hzHistory.Add(hz);
                    if (_hzHistory.Count > 20) _hzHistory.RemoveAt(0);

                    double sum = 0;
                    foreach (var h in _hzHistory) sum += h;
                    double avg = sum / _hzHistory.Count;
                    AvgHzText.Text = $"{avg:F0} Hz";
                    if (HzSummaryText != null) HzSummaryText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_HzSummary"), $"{avg:F0}", $"{_peakHz:F0}");
                }
                else
                {
                    CurrentHzText.Text = "0 Hz";
                    if (MouseCurrentHzSummaryText != null) MouseCurrentHzSummaryText.Text = "0 Hz";
                }
            }
        }

        private void ChatterThresholdSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ChatterThresholdValueText != null)
            {
                ChatterThresholdValueText.Text = $"{e.NewValue:F0} ms";
            }
        }

        private static void HighlightBorder(Border border, bool active)
        {
            if (active)
            {
                border.Background = GetThemeBrush("AccentFillColorDefaultBrush");
            }
            else
            {
                border.Background = GetThemeBrush("ControlFillColorDefaultBrush");
            }
        }

        #endregion

        #region Trajectory Canvas Drawing & Sensor Polling

        private void TrajectoryCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
            var props = e.GetCurrentPoint(TrajectoryCanvas).Properties;
            HandleMouseButtonPress(props);

            if (props.IsLeftButtonPressed)
            {
                _isDrawing = true;
                _lastPoint = e.GetCurrentPoint(TrajectoryCanvas).Position;
                TrajectoryCanvas.CapturePointer(e.Pointer);
            }
        }

        private void TrajectoryCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            _pollingEventCount++;
            if (_pollingWindowStartTimestamp == 0)
            {
                _pollingWindowStartTimestamp = Stopwatch.GetTimestamp();
            }

            if (!_isDrawing) return;

            var currentPoint = e.GetCurrentPoint(TrajectoryCanvas).Position;
            var line = new Line
            {
                X1 = _lastPoint.X,
                Y1 = _lastPoint.Y,
                X2 = currentPoint.X,
                Y2 = currentPoint.Y,
                Stroke = GetThemeBrush("AccentFillColorDefaultBrush"),
                StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            TrajectoryCanvas.Children.Add(line);
            _lastPoint = currentPoint;
        }

        private void TrajectoryCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
            if (_isDrawing)
            {
                _isDrawing = false;
                TrajectoryCanvas.ReleasePointerCapture(e.Pointer);
            }
            HandleMouseButtonRelease(e.GetCurrentPoint(TrajectoryCanvas).Properties);
        }

        private void ClearCanvasBtn_Click(object sender, RoutedEventArgs e)
        {
            TrajectoryCanvas.Children.Clear();
        }

        #endregion

        #region Top Controls & Reset

        private void ModeRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || KeyboardSection == null || MouseSection == null) return;

            if (ModeRadioButtons.SelectedIndex == 0)
            {
                MouseSection.Visibility = Visibility.Collapsed;
                KeyboardSection.Visibility = Visibility.Visible;
                FadeInKeyboardStoryboard?.Begin();
                KeyboardFocusArea?.Focus(FocusState.Programmatic);
                UpdateFocusVisual(true);
            }
            else
            {
                KeyboardSection.Visibility = Visibility.Collapsed;
                MouseSection.Visibility = Visibility.Visible;
                FadeInMouseStoryboard?.Begin();
                ReleaseAllMouseButtonVisuals();
            }
        }

        private void SoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Sound feedback preference toggle
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            // Reset Keyboard states
            _activeKeys.Clear();
            _testedKeys.Clear();
            _maxRollover = 0;
            _totalPresses = 0;
            _lastKeyTimestamp = 0;
            _lastPressedKeyId = string.Empty;

            foreach (var keyId in _keyVisualMap.Keys)
            {
                ApplyKeyVisualState(keyId, KeyVisualState.Default);
            }

            LastKeyText.Text = "-";
            LastKeyCodeText.Text = "VK: - | Scan: -";
            CurrentDownText.Text = "0";
            ActiveKeyListText.Text = LocalizationHelper.GetString("InputTesterPage_Key_None");
            MaxRolloverText.Text = "0";
            TotalPressesText.Text = "0";
            UpdateTestedKeysStat();
            KeyIntervalText.Text = "-";
            KeyChatterStatusText.Text = "OK";
            KeyChatterStatusText.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");

            // Reset Mouse states
            ResetMouseVisualsAndStats();
        }

        private void ResetMouseVisualsAndStats()
        {
            _mouseLeftCount = 0;
            _mouseRightCount = 0;
            _mouseMiddleCount = 0;
            _mouseSide1Count = 0;
            _mouseSide2Count = 0;
            _wheelUpCount = 0;
            _wheelDownCount = 0;
            _normalClicks = 0;
            _chatterClicks = 0;
            _lastMouseClickTimestamp = 0;
            _peakHz = 0;
            _hzHistory.Clear();

            if (MouseLeftCountText != null) MouseLeftCountText.Text = "0";
            if (MouseRightCountText != null) MouseRightCountText.Text = "0";
            if (Side1CountText != null) Side1CountText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ClickCountUnit"), 0);
            if (Side2CountText != null) Side2CountText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ClickCountUnit"), 0);
            if (WheelDeltaText != null) WheelDeltaText.Text = "0 / 0";
            if (NormalClicksText != null) NormalClicksText.Text = "0";
            if (ChatterClicksText != null)
            {
                ChatterClicksText.Text = "0";
                ChatterClicksText.Foreground = GetThemeBrush("TextFillColorPrimaryBrush");
            }
            if (LastClickIntervalText != null) LastClickIntervalText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_LastIntervalNone");
            if (CurrentHzText != null) CurrentHzText.Text = "0 Hz";
            if (AvgHzText != null) AvgHzText.Text = "0 Hz";
            if (PeakHzText != null) PeakHzText.Text = "0 Hz";

            // Mouse Summary Cards Reset
            if (LastMouseBtnText != null) LastMouseBtnText.Text = "-";
            if (LastMouseActionText != null) LastMouseActionText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_NoAction");
            if (MouseTotalClicksCountText != null) MouseTotalClicksCountText.Text = "0";
            if (MouseClicksBreakdownText != null) MouseClicksBreakdownText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ClicksBreakdown"), 0, 0, 0);
            if (MouseCurrentHzSummaryText != null) MouseCurrentHzSummaryText.Text = "0 Hz";
            if (HzSummaryText != null) HzSummaryText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_HzSummary"), 0, 0);
            if (WheelStepsSummaryText != null) WheelStepsSummaryText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_StepsUnit"), 0);
            if (WheelDeltaBreakdownText != null) WheelDeltaBreakdownText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_WheelBreakdown"), 0, 0, LocalizationHelper.GetString("InputTesterPage_Mouse_WheelStatus_OK"));
            if (ChatterStatusSummaryText != null)
            {
                ChatterStatusSummaryText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterStatus_OK");
                ChatterStatusSummaryText.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
            }
            if (ChatterCountsText != null) ChatterCountsText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterCounts"), 0, 0);

            if (WheelStatusIcon != null)
            {
                WheelStatusIcon.Glyph = "\uE73E";
                WheelStatusIcon.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
            }
            if (WheelStatusText != null)
            {
                WheelStatusText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_WheelStatus_OK");
                WheelStatusText.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
            }

            if (ChatterAlertBanner != null) ChatterAlertBanner.Visibility = Visibility.Collapsed;
            if (TrajectoryCanvas != null) TrajectoryCanvas.Children.Clear();
            ReleaseAllMouseButtonVisuals();
        }

        #endregion
    }
}
