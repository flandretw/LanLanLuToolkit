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
using Microsoft.UI.Xaml.Navigation;
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
        private readonly Dictionary<int, long> _lastClickTimestampByButton = new();
        private int _lastWheelDirection = 0; // 1 = Up, -1 = Down
        private int _consecutiveWheelSameDir = 0;
        private long _lastWheelTimestamp = 0;

        // Polling Rate Tracking
        private int _pollingEventCount = 0;
        private long _pollingWindowStartTimestamp = 0;
        private double _peakHz = 0;
        private readonly List<double> _hzHistory = new();
        private readonly DispatcherTimer _hzUpdateTimer = new();

        // Canvas Trajectory Drawing
        private bool _isDrawing = false;
        private Point _lastPoint;
        private Brush? _cachedAccentBrush;

        private DispatcherTimer? _wheelVisualResetTimer;

        private readonly PointerEventHandler _pointerPressedHandler;
        private readonly PointerEventHandler _pointerReleasedHandler;
        private readonly PointerEventHandler _pointerMovedHandler;
        private readonly PointerEventHandler _pointerCanceledHandler;
        private readonly PointerEventHandler _pointerCaptureLostHandler;
        private bool _isLoaded = false;
        private int SelectedModeIndex => (InputModeSelectorBar?.SelectedItem == MouseSelectorItem) ? 1 : 0;

        public InputTesterPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;

            this.ActualThemeChanged += (s, e) =>
            {
                BuildKeyboardLayout();
                UpdateFocusVisual(ReferenceEquals(FocusManager.GetFocusedElement(this.XamlRoot), KeyboardFocusArea));
                UpdateMouseThemeVisuals();
            };

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
            if (KeyboardSelectorItem != null && string.IsNullOrEmpty(KeyboardSelectorItem.Text))
            {
                KeyboardSelectorItem.Text = LocalizationHelper.GetString("InputTesterPage_Tab_Keyboard.Text") ?? "鍵盤測試";
            }
            if (MouseSelectorItem != null && string.IsNullOrEmpty(MouseSelectorItem.Text))
            {
                MouseSelectorItem.Text = LocalizationHelper.GetString("InputTesterPage_Tab_Mouse.Text") ?? "滑鼠測試";
            }
            if (InputModeSelectorBar != null && InputModeSelectorBar.SelectedItem == null)
            {
                InputModeSelectorBar.SelectedItem = KeyboardSelectorItem;
            }

            if (_keyVisualMap.Count == 0)
            {
                BuildKeyboardLayout();
            }
            _hzUpdateTimer.Start();
            KeyboardFocusArea?.Focus(FocusState.Programmatic);
            UpdateFocusVisual(true);
            ResetKeyboardVisualsAndStats();
            ResetMouseVisualsAndStats();

            this.AddHandler(UIElement.PointerPressedEvent, _pointerPressedHandler, true);
            this.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, true);
            this.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, true);
            this.AddHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler, true);
            this.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, true);

            UpdatePageLayoutBounds(this.ActualWidth, this.ActualHeight);
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
            if (SelectedModeIndex == 0)
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
            if (SelectedModeIndex == 0)
            {
                KeyboardFocusArea_KeyUp(sender, e);
            }
        }

        private bool IsCurrentThemeLight()
        {
            try
            {
                string savedTheme = SettingsService.GetTheme();
                if (string.Equals(savedTheme, "Light", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(savedTheme, "Dark", StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { }

            if (this.ActualTheme == ElementTheme.Light) return true;
            if (this.ActualTheme == ElementTheme.Dark) return false;

            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                var bg = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
                return (bg.R > 128 && bg.G > 128 && bg.B > 128);
            }
            catch
            {
                return Application.Current.RequestedTheme == ApplicationTheme.Light;
            }
        }

        private Brush GetThemeBrush(string resourceKey)
        {
            if (this.Resources.TryGetValue(resourceKey, out var pageRes) && pageRes is Brush pb)
            {
                return pb;
            }

            bool isLight = IsCurrentThemeLight();
            string themeKey = isLight ? "Light" : "Default";

            if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var tdObj) && tdObj is ResourceDictionary td)
            {
                if (td.TryGetValue(resourceKey, out var res) && res is Brush b)
                {
                    return b;
                }
            }

            foreach (var md in Application.Current.Resources.MergedDictionaries)
            {
                if (md.ThemeDictionaries.TryGetValue(themeKey, out var mtdObj) && mtdObj is ResourceDictionary mtd)
                {
                    if (mtd.TryGetValue(resourceKey, out var mtdRes) && mtdRes is Brush mtdb)
                    {
                        return mtdb;
                    }
                }
            }

            // In Dark theme only, check root resources
            if (!isLight && Application.Current.Resources.TryGetValue(resourceKey, out var appRes) && appRes is Brush ab)
            {
                return ab;
            }

            return GetFallbackBrush(resourceKey, isLight);
        }

        private static Brush GetFallbackBrush(string resourceKey, bool isLight)
        {
            Color accentColor;
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                accentColor = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            }
            catch
            {
                accentColor = Color.FromArgb(255, 0, 120, 212);
            }

            return resourceKey switch
            {
                "TextFillColorPrimaryBrush" => new SolidColorBrush(isLight ? Color.FromArgb(255, 27, 27, 27) : Color.FromArgb(255, 255, 255, 255)),
                "TextFillColorSecondaryBrush" => new SolidColorBrush(isLight ? Color.FromArgb(255, 92, 92, 92) : Color.FromArgb(255, 197, 197, 197)),
                "TextFillColorTertiaryBrush" => new SolidColorBrush(isLight ? Color.FromArgb(255, 140, 140, 140) : Color.FromArgb(255, 120, 120, 120)),
                "TextOnAccentFillColorPrimaryBrush" => new SolidColorBrush(Colors.White),
                "TextOnAccentFillColorSecondaryBrush" => new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                "AccentFillColorDefaultBrush" => new SolidColorBrush(accentColor),
                "SystemFillColorSuccessBrush" => new SolidColorBrush(isLight ? Color.FromArgb(255, 16, 124, 65) : Color.FromArgb(255, 108, 203, 95)),
                "SystemFillColorCriticalBrush" => new SolidColorBrush(isLight ? Color.FromArgb(255, 196, 43, 28) : Color.FromArgb(255, 255, 153, 164)),
                "ControlFillColorDefaultBrush" => new SolidColorBrush(isLight ? Colors.White : Color.FromArgb(255, 45, 45, 45)),
                "ControlFillColorSecondaryBrush" => new SolidColorBrush(isLight ? Color.FromArgb(255, 243, 243, 243) : Color.FromArgb(255, 56, 56, 56)),
                "ControlAltFillColorSecondaryBrush" => new SolidColorBrush(isLight ? Color.FromArgb(20, 0, 0, 0) : Color.FromArgb(30, 255, 255, 255)),
                "CardStrokeColorDefaultBrush" => new SolidColorBrush(isLight ? Color.FromArgb(255, 222, 222, 222) : Color.FromArgb(255, 60, 60, 60)),
                _ => new SolidColorBrush(isLight ? Color.FromArgb(255, 27, 27, 27) : Color.FromArgb(255, 255, 255, 255))
            };
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
                KeyboardViewbox.MaxHeight = 275;
            }

            foreach (var testedKeyId in _testedKeys)
            {
                if (_keyVisualMap.ContainsKey(testedKeyId))
                {
                    ApplyKeyVisualState(testedKeyId, KeyVisualState.Tested);
                }
            }

            UpdateTestedKeysStat();
            UpdatePageLayoutBounds(this.ActualWidth, this.ActualHeight);
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
                Tag = key.Id,
                UseLayoutRounding = false,
                Style = (Style)Resources["KeycapDefaultStyle"]
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
                    Style = (Style)Resources["KeycapSubTextDefaultStyle"]
                };
                stack.Children.Add(subText);
            }

            var mainText = new TextBlock
            {
                Text = key.DisplayLabel,
                FontSize = key.DisplayLabel.Length > 3 ? 10 : 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Resources["KeycapMainTextDefaultStyle"]
            };
            stack.Children.Add(mainText);

            border.Child = stack;
            return border;
        }

        private void PlayKeySound()
        {
            if (!SettingsService.GetSoundEffectsEnabled()) return;
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
            LastKeyCodeText.Text = FormatKeyCode((int)e.Key, e.KeyStatus.ScanCode);
            CurrentDownText.Text = _activeKeys.Count.ToString();
            ActiveKeyListText.Text = string.Join(", ", _activeKeys);
            MaxRolloverText.Text = _maxRollover.ToString();
            TotalPressesText.Text = _totalPresses.ToString();
            UpdateTestedKeysStat();

            if (intervalMs > 0 && isChatter)
            {
                LastKeyCodeText.Text = FormatKeyCode((int)e.Key, e.KeyStatus.ScanCode) + " (" + string.Format(LocalizationHelper.GetString("InputTesterPage_Key_ChatterWarning"), $"{intervalMs:F0}") + ")";
                LastKeyCodeText.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
            }
            else
            {
                LastKeyCodeText.Text = FormatKeyCode((int)e.Key, e.KeyStatus.ScanCode);
                LastKeyCodeText.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
            }
        }

        private static string FormatKeyCode(int vk, uint scanCode)
        {
            string format = LocalizationHelper.GetString("InputTesterPage_Key_CodeFormat");
            if (string.IsNullOrEmpty(format) || format == "InputTesterPage_Key_CodeFormat")
            {
                format = "VK: {0} (0x{1:X2}) | Scan: 0x{2:X2}";
            }
            return string.Format(format, vk, vk, scanCode);
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
                LastKeyText.Text = _keyModelMap.TryGetValue("Snapshot", out var snapshotModel) ? snapshotModel.DisplayLabel : "PrtSc";
                LastKeyCodeText.Text = FormatKeyCode(44, 0x37);
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

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageLayoutBounds(e.NewSize.Width, e.NewSize.Height);
        }

        private void RootContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageLayoutBounds(this.ActualWidth, this.ActualHeight);
        }

        private void KeyboardFocusArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageLayoutBounds(this.ActualWidth, this.ActualHeight);
        }

        private void UpdatePageLayoutBounds(double viewportWidth, double viewportHeight = 0)
        {
            if (viewportWidth <= 0) viewportWidth = this.ActualWidth;
            if (viewportHeight <= 0) viewportHeight = this.ActualHeight;
            if (viewportWidth <= 0) return;

            if (RootContentGrid != null)
            {
                RootContentGrid.MaxWidth = viewportWidth;
            }

            if (KeyboardViewbox != null)
            {
                // Horizontal width constraint
                double availableWidth = viewportWidth - 120;
                if (availableWidth > 50)
                {
                    KeyboardViewbox.MaxWidth = availableWidth;
                }

                // Vertical height constraint to strictly prevent overflowing below
                if (viewportHeight > 300)
                {
                    // Total top fixed elements (Page Padding ~36, Header ~44, Settings Cards ~92, Stats ~76, Spacings ~44, Card Header ~58) ≈ 350px
                    double availableHeight = viewportHeight - 350;
                    KeyboardViewbox.MaxHeight = Math.Max(160, Math.Min(275, availableHeight));
                }
                else
                {
                    KeyboardViewbox.MaxHeight = 275;
                }
            }
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
                KeyboardFocusArea.Opacity = 1.0;

                FocusStatusBadge.Style = (Style)Resources["FocusBadgeActiveStyle"];
                if (FocusStatusIcon != null)
                {
                    FocusStatusIcon.Glyph = "\uE73E"; // Fluent Checkmark
                    FocusStatusIcon.Style = (Style)Resources["FocusStatusIconActiveStyle"];
                }
                string focusedText = LocalizationHelper.GetString("InputTesterPage_FocusStatus_Focused.Text");
                FocusStatusText.Text = !string.IsNullOrEmpty(focusedText) ? focusedText : "Ready";
                FocusStatusText.Style = (Style)Resources["FocusStatusTextActiveStyle"];
            }
            else
            {
                KeyboardFocusArea.BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush");
                KeyboardFocusArea.BorderThickness = new Thickness(1);
                KeyboardFocusArea.Opacity = 0.85;

                FocusStatusBadge.Style = (Style)Resources["FocusBadgeDefaultStyle"];
                if (FocusStatusIcon != null)
                {
                    FocusStatusIcon.Glyph = "\uE765"; // Fluent Keyboard
                    FocusStatusIcon.Style = (Style)Resources["FocusStatusIconDefaultStyle"];
                }
                string unfocusedText = LocalizationHelper.GetString("InputTesterPage_FocusStatus_Unfocused.Text");
                FocusStatusText.Text = !string.IsNullOrEmpty(unfocusedText) ? unfocusedText : "Focus";
                FocusStatusText.Style = (Style)Resources["FocusStatusTextDefaultStyle"];
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
                    border.Style = (Style)Resources["KeycapPressedStyle"];
                    SetKeycapTextColors(border, KeyVisualState.Pressed);
                    break;

                case KeyVisualState.Tested:
                    border.Style = (Style)Resources["KeycapTestedStyle"];
                    SetKeycapTextColors(border, KeyVisualState.Tested);
                    break;

                case KeyVisualState.Warning:
                    border.Style = (Style)Resources["KeycapWarningStyle"];
                    SetKeycapTextColors(border, KeyVisualState.Warning);
                    break;

                case KeyVisualState.Default:
                default:
                    border.Style = (Style)Resources["KeycapDefaultStyle"];
                    SetKeycapTextColors(border, KeyVisualState.Default);
                    break;
            }
        }

        private void SetKeycapTextColors(Border border, KeyVisualState state)
        {
            if (border.Child is not StackPanel stack) return;

            var mainStyle = state switch
            {
                KeyVisualState.Pressed => (Style)Resources["KeycapMainTextPressedStyle"],
                KeyVisualState.Warning => (Style)Resources["KeycapMainTextPressedStyle"],
                KeyVisualState.Tested => (Style)Resources["KeycapMainTextTestedStyle"],
                _ => (Style)Resources["KeycapMainTextDefaultStyle"]
            };

            var subStyle = state switch
            {
                KeyVisualState.Pressed => (Style)Resources["KeycapSubTextPressedStyle"],
                KeyVisualState.Warning => (Style)Resources["KeycapSubTextPressedStyle"],
                _ => (Style)Resources["KeycapSubTextDefaultStyle"]
            };

            for (int i = 0; i < stack.Children.Count; i++)
            {
                if (stack.Children[i] is TextBlock tb)
                {
                    tb.ClearValue(TextBlock.ForegroundProperty);
                    if (stack.Children.Count > 1 && i == 0)
                    {
                        tb.Style = subStyle;
                    }
                    else
                    {
                        tb.Style = mainStyle;
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
            int buttonId = 0; // 1: Left, 2: Right, 3: Middle, 4: Side1, 5: Side2

            if (updateKind == PointerUpdateKind.LeftButtonPressed || (updateKind == PointerUpdateKind.Other && props.IsLeftButtonPressed))
            {
                buttonId = 1;
                _mouseLeftCount++;
                HighlightBorder(MouseLeftBtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_LeftButton");
            }
            else if (updateKind == PointerUpdateKind.RightButtonPressed || (updateKind == PointerUpdateKind.Other && props.IsRightButtonPressed))
            {
                buttonId = 2;
                _mouseRightCount++;
                HighlightBorder(MouseRightBtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_RightButton");
            }
            else if (updateKind == PointerUpdateKind.MiddleButtonPressed || (updateKind == PointerUpdateKind.Other && props.IsMiddleButtonPressed))
            {
                buttonId = 3;
                _mouseMiddleCount++;
                HighlightBorder(MouseMiddleBtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_MiddleButton");
            }
            else if (updateKind == PointerUpdateKind.XButton1Pressed || (updateKind == PointerUpdateKind.Other && props.IsXButton1Pressed))
            {
                buttonId = 4;
                _mouseSide1Count++;
                HighlightBorder(MouseSide1BtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_Side1Button");
            }
            else if (updateKind == PointerUpdateKind.XButton2Pressed || (updateKind == PointerUpdateKind.Other && props.IsXButton2Pressed))
            {
                buttonId = 5;
                _mouseSide2Count++;
                HighlightBorder(MouseSide2BtnVisual, true);
                lastBtnName = LocalizationHelper.GetString("InputTesterPage_Mouse_Side2Button");
            }

            if (buttonId == 0) return;

            PlayKeySound();

            int totalClicks = _mouseLeftCount + _mouseRightCount + _mouseMiddleCount + _mouseSide1Count + _mouseSide2Count;

            // Update Mouse Summary Cards
            if (LastMouseBtnText != null) LastMouseBtnText.Text = lastBtnName;
            if (MouseTotalClicksCountText != null) MouseTotalClicksCountText.Text = totalClicks.ToString();
            if (MouseClicksBreakdownText != null) MouseClicksBreakdownText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ClicksBreakdown"), _mouseLeftCount, _mouseRightCount, _mouseMiddleCount);

            // Automatically evaluate switch chatter for this specific physical switch
            ProcessMouseClickChatterCheck(buttonId);
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
            if (SelectedModeIndex != 1) return;

            if (e.OriginalSource is DependencyObject dep)
            {
                if (IsDescendantOf<Button>(dep) || IsDescendantOf<Slider>(dep) || IsDescendantOf<ToggleSwitch>(dep) || IsDescendantOf<ComboBox>(dep) || IsDescendantOf<ComboBoxItem>(dep) || IsDescendantOf<SelectorBar>(dep) || IsDescendantOf<SelectorBarItem>(dep))
                {
                    return;
                }
            }

            var props = e.GetCurrentPoint(this).Properties;
            HandleMouseButtonPress(props);
        }

        private void OnGlobalPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (SelectedModeIndex != 1) return;

            var props = e.GetCurrentPoint(this).Properties;
            HandleMouseButtonRelease(props);
        }

        private void OnGlobalPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (SelectedModeIndex != 1) return;

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

        private void ProcessMouseClickChatterCheck(int buttonId)
        {
            long now = Stopwatch.GetTimestamp();
            double intervalMs = 0;

            if (_lastClickTimestampByButton.TryGetValue(buttonId, out long lastTimestamp) && lastTimestamp > 0)
            {
                intervalMs = (now - lastTimestamp) * 1000.0 / Stopwatch.Frequency;
            }
            _lastClickTimestampByButton[buttonId] = now;

            // Industry standard: chatter / switch contact bounce is typically < 50ms on the same switch
            const double threshold = 50.0;
            bool isChatter = intervalMs > 0 && intervalMs < threshold;

            if (isChatter)
            {
                _chatterClicks++;
                if (ChatterStatusSummaryText != null)
                {
                    ChatterStatusSummaryText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterStatus_Warning");
                    ChatterStatusSummaryText.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                }
            }
            else
            {
                _normalClicks++;
                if (_chatterClicks == 0 && ChatterStatusSummaryText != null)
                {
                    ChatterStatusSummaryText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterStatus_OK");
                    ChatterStatusSummaryText.Foreground = GetThemeBrush("TextFillColorPrimaryBrush");
                }
            }

            if (ChatterCountsText != null)
            {
                ChatterCountsText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterCounts"), _normalClicks, _chatterClicks);
            }

            if (LastMouseActionText != null)
            {
                if (intervalMs > 0)
                {
                    if (isChatter)
                    {
                        LastMouseActionText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_IntervalAction"), $"{intervalMs:F1}")
                            + " (" + LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterStatus_Warning") + ")";
                        LastMouseActionText.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                    }
                    else
                    {
                        LastMouseActionText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_IntervalAction"), $"{intervalMs:F1}");
                        LastMouseActionText.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
                    }
                }
                else
                {
                    LastMouseActionText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_NoAction");
                    LastMouseActionText.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
                }
            }
        }

        private void TrajectoryCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
            int delta = e.GetCurrentPoint(sender as UIElement).Properties.MouseWheelDelta;
            HandleWheelScroll(delta);
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

            // Detect Wheel Jitter (Encoder contact bounce / rollback):
            // Hardware jitter occurs when an encoder contact bounce generates an erratic reverse pulse
            // during continuous scrolling in a single direction.
            // Human physical direction changes:
            // 1) Human finger needs to stop and reverse motion (takes >= 50ms).
            // 2) An intentional direction switch is sustained across multiple ticks in the new direction.
            // A false bounce occurs when:
            // - Previously scrolling rapidly in the opposite direction (_consecutiveWheelSameDir >= 3)
            // - The reversal arrives in an impossibly short physical time window (< 40ms)
            // - If the pause was > 150ms, it is always a new distinct scroll gesture, not jitter.
            long now = Stopwatch.GetTimestamp();
            double elapsedMs = _lastWheelTimestamp > 0
                ? (now - _lastWheelTimestamp) * 1000.0 / Stopwatch.Frequency
                : 1000.0;
            _lastWheelTimestamp = now;

            bool isJitter = false;
            if (elapsedMs > 150.0)
            {
                // Pause detected: this is a fresh user scroll gesture, not a continuation/bounce
                _consecutiveWheelSameDir = 1;
            }
            else if (currentDir == _lastWheelDirection)
            {
                // Continuing in the same direction: normal valid scroll
                _consecutiveWheelSameDir++;
            }
            else
            {
                // Direction changed within an active scroll stream (<= 150ms)
                // Only flag as jitter if it reversed in < 40ms after a sustained stream of >= 3 ticks
                if (_lastWheelDirection != 0 && _consecutiveWheelSameDir >= 3 && elapsedMs < 40.0)
                {
                    isJitter = true;
                }
                _consecutiveWheelSameDir = 1;
            }

            _lastWheelDirection = currentDir;

            if (LastMouseBtnText != null)
            {
                LastMouseBtnText.Text = currentDir > 0 ? LocalizationHelper.GetString("InputTesterPage_Mouse_WheelUp") : LocalizationHelper.GetString("InputTesterPage_Mouse_WheelDown");
            }
            if (LastMouseActionText != null)
            {
                string deltaStr = delta > 0 ? $"+{delta}" : $"{delta}";
                if (isJitter)
                {
                    LastMouseActionText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_WheelScrollAction"), deltaStr) + " (" + LocalizationHelper.GetString("InputTesterPage_Mouse_WheelStatus_Jitter") + ")";
                    LastMouseActionText.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                }
                else
                {
                    LastMouseActionText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_WheelScrollAction"), deltaStr);
                    LastMouseActionText.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
                }
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
                    if (MouseCurrentHzSummaryText != null) MouseCurrentHzSummaryText.Text = $"{hz:F0} Hz";

                    if (hz > _peakHz)
                    {
                        _peakHz = hz;
                    }

                    _hzHistory.Add(hz);
                    if (_hzHistory.Count > 20) _hzHistory.RemoveAt(0);

                    double sum = 0;
                    foreach (var h in _hzHistory) sum += h;
                    double avg = sum / _hzHistory.Count;
                    if (HzSummaryText != null) HzSummaryText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_HzSummary"), $"{avg:F0}", $"{_peakHz:F0}");
                }
                else
                {
                    if (MouseCurrentHzSummaryText != null) MouseCurrentHzSummaryText.Text = "0 Hz";
                }
            }
        }

        private void HighlightBorder(Border? border, bool active)
        {
            if (border == null) return;

            if (active)
            {
                border.Background = GetThemeBrush("AccentFillColorDefaultBrush");
                border.BorderBrush = GetThemeBrush("AccentFillColorDefaultBrush");
                SetMouseButtonContentState(border, true);
            }
            else
            {
                if (ReferenceEquals(border, MouseMiddleBtnVisual))
                {
                    border.Background = GetThemeBrush("ControlSolidFillColorDefaultBrush");
                }
                else
                {
                    border.Background = GetThemeBrush("ControlFillColorDefaultBrush");
                }
                border.BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush");
                SetMouseButtonContentState(border, false);
            }
        }

        private void SetMouseButtonContentState(Border border, bool active)
        {
            if (ReferenceEquals(border, MouseMiddleBtnVisual) && WheelMiddleIcon != null)
            {
                WheelMiddleIcon.Foreground = active
                    ? GetThemeBrush("TextOnAccentFillColorPrimaryBrush")
                    : GetThemeBrush("TextFillColorSecondaryBrush");
            }
        }

        private void UpdateMouseThemeVisuals()
        {
            ReleaseAllMouseButtonVisuals();
            _cachedAccentBrush = null;
            if (WheelUpArrow != null) WheelUpArrow.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
            if (WheelDownArrow != null) WheelDownArrow.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
        }

        #endregion

        #region Trajectory Canvas Drawing & Sensor Polling

        private void TrajectoryCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(TrajectoryCanvas).Properties;
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
            if (TrajectoryCanvas.Children.Count > 400)
            {
                TrajectoryCanvas.Children.RemoveAt(0);
            }
            var line = new Line
            {
                X1 = _lastPoint.X,
                Y1 = _lastPoint.Y,
                X2 = currentPoint.X,
                Y2 = currentPoint.Y,
                Stroke = _cachedAccentBrush ??= GetThemeBrush("AccentFillColorDefaultBrush"),
                StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            TrajectoryCanvas.Children.Add(line);
            _lastPoint = currentPoint;
        }

        private void TrajectoryCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDrawing)
            {
                _isDrawing = false;
                TrajectoryCanvas.ReleasePointerCapture(e.Pointer);
            }
        }

        private void MouseLayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || MouseLayoutComboBox == null || MouseLayoutTitleText == null) return;

            bool hasSideButtons = MouseLayoutComboBox.SelectedIndex == 1;

            if (MouseSide1BtnVisual != null)
            {
                MouseSide1BtnVisual.Visibility = hasSideButtons ? Visibility.Visible : Visibility.Collapsed;
            }
            if (MouseSide2BtnVisual != null)
            {
                MouseSide2BtnVisual.Visibility = hasSideButtons ? Visibility.Visible : Visibility.Collapsed;
            }

            MouseLayoutTitleText.Text = hasSideButtons
                ? LocalizationHelper.GetString("InputTesterPage_Mouse_StageTitle_5Key")
                : LocalizationHelper.GetString("InputTesterPage_Mouse_StageTitle_3Key");
        }

        #endregion

        #region Top Controls & Reset

        private void InputModeSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (!_isLoaded || KeyboardSection == null || MouseSection == null) return;

            if (sender.SelectedItem == KeyboardSelectorItem)
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

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            ResetKeyboardVisualsAndStats();
            ResetMouseVisualsAndStats();
        }

        private void ResetKeyboardVisualsAndStats()
        {
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

            if (LastKeyText != null) LastKeyText.Text = "-";
            if (LastKeyCodeText != null)
            {
                LastKeyCodeText.Text = LocalizationHelper.GetString("InputTesterPage_Key_CodeEmpty");
                LastKeyCodeText.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
            }
            if (CurrentDownText != null) CurrentDownText.Text = "0";
            if (ActiveKeyListText != null) ActiveKeyListText.Text = LocalizationHelper.GetString("InputTesterPage_Key_None");
            if (MaxRolloverText != null) MaxRolloverText.Text = "0";
            if (TotalPressesText != null) TotalPressesText.Text = "0";
            UpdateTestedKeysStat();
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
            _lastClickTimestampByButton.Clear();
            _lastWheelDirection = 0;
            _consecutiveWheelSameDir = 0;
            _lastWheelTimestamp = 0;
            _peakHz = 0;
            _hzHistory.Clear();

            // Mouse Summary Cards Reset
            if (LastMouseBtnText != null) LastMouseBtnText.Text = "-";
            if (LastMouseActionText != null)
            {
                LastMouseActionText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_NoAction");
                LastMouseActionText.Foreground = GetThemeBrush("TextFillColorTertiaryBrush");
            }
            if (MouseTotalClicksCountText != null) MouseTotalClicksCountText.Text = "0";
            if (MouseClicksBreakdownText != null) MouseClicksBreakdownText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ClicksBreakdown"), 0, 0, 0);
            if (MouseCurrentHzSummaryText != null) MouseCurrentHzSummaryText.Text = "0 Hz";
            if (HzSummaryText != null) HzSummaryText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_HzSummary"), 0, 0);
            if (ChatterStatusSummaryText != null)
            {
                ChatterStatusSummaryText.Text = LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterStatus_OK");
                ChatterStatusSummaryText.Foreground = GetThemeBrush("TextFillColorPrimaryBrush");
            }
            if (ChatterCountsText != null) ChatterCountsText.Text = string.Format(LocalizationHelper.GetString("InputTesterPage_Mouse_ChatterCounts"), 0, 0);

            if (TrajectoryCanvas != null) TrajectoryCanvas.Children.Clear();
            ReleaseAllMouseButtonVisuals();
        }

        #endregion
    }
}
