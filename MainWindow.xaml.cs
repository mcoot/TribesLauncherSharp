using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TribesLauncherSharp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow() {
            DataContext = new Config();
            InitializeComponent();
        }

        private void LauncherButton_Click(object sender, RoutedEventArgs e)
        {
            
        }

        private void MainAppWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists("launcherconfig.yaml"))
            {
                try
                {
                    DataContext = Config.Load("launcherconfig.yaml");
                } catch (Exception ex)
                {
                    MessageBox.Show("Failed to read launcher configuration: " + ex.Message, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            if (((Config)DataContext).Injection.Mode == InjectMode.Automatic)
            {
                InjectionModeAutoRadio.IsChecked = true;
            } else
            {
                InjectionModeManualRadio.IsChecked = true;
            }
        }

        private void MainAppWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                ((Config)DataContext).Save("launcherconfig.yaml");
            } catch (Exception ex)
            {
                MessageBox.Show("Failed to save launcher configuration: " + ex.Message, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InjectionModeManualRadio_Checked(object sender, RoutedEventArgs e)
        {
            ((Config)DataContext).Injection.Mode = InjectMode.Manual;
        }

        private void InjectionModeAutoRadio_Checked(object sender, RoutedEventArgs e)
        {
            ((Config)DataContext).Injection.Mode = InjectMode.Automatic;
        }
    }
}
