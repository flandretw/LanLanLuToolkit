using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using lanlanlu_toolkit.Services;

namespace lanlanlu_toolkit.Views
{
    public sealed partial class HomePage : Page, INotifyPropertyChanged
    {
        private double _heroHeight = 400;
        
        public double HeroHeight
        {
            get => _heroHeight;
            set
            {
                if (_heroHeight != value)
                {
                    _heroHeight = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ContentMargin));
                }
            }
        }

        public Thickness ContentMargin => new Thickness(36, HeroHeight + 24, 36, 36);

        public HomePage()
        {
            this.InitializeComponent();
            UpdateGreeting();
            this.SizeChanged += HomePage_SizeChanged;
        }

        private void HomePage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateLayoutProportions(e.NewSize.Height);
        }

        private void UpdateLayoutProportions(double windowHeight)
        {
            // 視窗高度小於 800px 時佔一半，否則佔三分之一
            HeroHeight = windowHeight < 800 ? windowHeight * 0.5 : windowHeight * 0.33;
        }

        private void UpdateGreeting()
        {
            var hour = DateTime.Now.Hour;
            string greetingKey = hour switch
            {
                >= 0 and < 5 => "Greeting_LateNight",
                >= 5 and < 12 => "Greeting_Morning",
                >= 12 and < 18 => "Greeting_Afternoon",
                _ => "Greeting_Evening"
            };
            GreetingText.Text = LocalizationHelper.GetString(greetingKey);
        }

        private void GoToPerformance_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PerformancePage));
        private void GoToSettings_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
