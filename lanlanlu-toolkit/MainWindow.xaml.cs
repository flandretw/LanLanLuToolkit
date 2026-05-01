using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;

using Microsoft.UI.Xaml.Controls.Primitives;

using Microsoft.UI.Xaml.Data;

using Microsoft.UI.Xaml.Input;

using Microsoft.UI.Xaml.Media;

using Microsoft.UI.Xaml.Navigation;

using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Foundation;

using Windows.Foundation.Collections;

using lanlanlu_toolkit.Views;



// To learn more about WinUI, the WinUI project structure,

// and more about our project templates, see: http://aka.ms/winui-project-info.



namespace lanlanlu_toolkit

{

    /// <summary>

    /// An empty window that can be used on its own or navigated to within a Frame.

    /// </summary>

    public sealed partial class MainWindow : Window

    {

        public MainWindow()
        {
            this.InitializeComponent();
            
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            // 設定應用程式視窗圖示
            var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            this.AppWindow.SetIcon(iconPath);
        }



        private void NavView_Loaded(object sender, RoutedEventArgs e)

        {

            NavView.SelectedItem = NavView.MenuItems[0];

            ContentFrame.Navigate(typeof(HomePage));

        }



        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)

        {

            if (args.IsSettingsInvoked)

            {

                ContentFrame.Navigate(typeof(SettingsPage));

            }

            else

            {

                var tag = args.InvokedItemContainer.Tag?.ToString();

                

                switch (tag)

                {

                    case "HomePage":

                        ContentFrame.Navigate(typeof(HomePage));

                        break;

                    case "TestToolPage":

                        ContentFrame.Navigate(typeof(TestToolPage));

                        break;

                }

            }

        }

    }

}

