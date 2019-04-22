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
    public enum LauncherStatus
    {
        READY_TO_LAUNCH,
        UPDATE_REQUIRED,
        UPDATE_IN_PROGRESS,
        READY_TO_INJECT,
        INJECTED
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Updater TAModsUpdater { get; set; }

        private LauncherStatus Status { get; set; }

        public MainWindow() {
            Status = LauncherStatus.READY_TO_LAUNCH;

            DataContext = new Config();
            InitializeComponent();

            string configPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal) + "/My Games/Tribes Ascend/TribesGame/config/";
            TAModsUpdater = new Updater(((Config)DataContext).UpdateUrl, ".", configPath);

            // Add event handlers
            TAModsUpdater.OnUpdateComplete += OnUpdateFinished;
            TAModsUpdater.OnProgressTick += OnUpdateProgressTick;
        }

        #region Helper Functions
        private void BeginUpdate()
        {
            if (Status == LauncherStatus.UPDATE_IN_PROGRESS) return;

            // Run the update asynchronously
            TAModsUpdater.PerformUpdate().FireAndForget((ex) =>
            {
                MessageBox.Show("Failed to complete update: " + ex.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });

            Status = LauncherStatus.UPDATE_IN_PROGRESS;
        }

        private void LaunchGame()
        {

        }

        private void Inject()
        {

        }
        #endregion

        #region Event Handlers
        private void OnUpdateFinished(object sender, EventArgs e)
        {
            Status = LauncherStatus.READY_TO_LAUNCH;
        }

        private void OnUpdateProgressTick(object sender, Updater.OnProgressTickEventArgs e)
        {
            UpdateProgressBar.Value = 100 * e.Proportion;
        }

        private void LauncherButton_Click(object sender, RoutedEventArgs e)
        {
            switch (Status)
            {
                case LauncherStatus.READY_TO_LAUNCH:
                    LaunchGame();
                    break;
                case LauncherStatus.READY_TO_INJECT:
                    Inject();
                    break;
                case LauncherStatus.INJECTED:
                    // No action
                    break;
                case LauncherStatus.UPDATE_REQUIRED:
                    BeginUpdate();
                    break;
                case LauncherStatus.UPDATE_IN_PROGRESS:
                    // No action
                    break;
            }
        }

        private void MainAppWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists("launcherconfig.yaml"))
            {
                try
                {
                    DataContext = Config.Load("launcherconfig.yaml");
                    TAModsUpdater.RemoteBaseUrl = ((Config)DataContext).UpdateUrl;
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
    #endregion
}
