using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        WAITING_TO_INJECT,
        INJECTED
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        System.Timers.Timer AutoInjectTimer { get; set; }

        private Updater TAModsUpdater { get; set; }
        private InjectorLauncher TALauncher { get; set; }

        private News TAModsNews { get; set; }

        private LauncherStatus Status { get; set; }

        private int lastLaunchedProcessId { get; set; } = 0;

        public MainWindow() {
            Status = LauncherStatus.READY_TO_LAUNCH;

            DataContext = new Config();
            InitializeComponent();
            TAModsNews = new News();

            AutoInjectTimer = new System.Timers.Timer();
            AutoInjectTimer.AutoReset = false;
            AutoInjectTimer.Elapsed += OnAutoInjectTimerElapsed;

            string configPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal) + "/My Games/Tribes Ascend/TribesGame/config/";
            TAModsUpdater = new Updater(((Config)DataContext).UpdateUrl, ".", configPath);

            // Add event handlers
            TAModsUpdater.OnUpdateComplete += OnUpdateFinished;
            TAModsUpdater.OnProgressTick += OnUpdateProgressTick;

            TALauncher = new InjectorLauncher();
            TALauncher.OnTargetProcessLaunched += OnProcessLaunched;
            TALauncher.OnTargetProcessEnded += OnProcessEnded;
            TALauncher.OnTargetPollingException += OnProcessPollingException;
        }

        #region Helper Functions
        private void SetStatus(LauncherStatus status)
        {
            Status = status;
            switch (Status)
            {
                case LauncherStatus.UPDATE_REQUIRED:
                    LauncherButton.Content = "Update";
                    LauncherButton.IsEnabled = true;
                    break;
                case LauncherStatus.UPDATE_IN_PROGRESS:
                    LauncherButton.Content = "Updating...";
                    LauncherButton.IsEnabled = false;
                    break;
                case LauncherStatus.READY_TO_LAUNCH:
                    LauncherButton.Content = "Launch";
                    LauncherButton.IsEnabled = true;
                    break;
                case LauncherStatus.READY_TO_INJECT:
                    LauncherButton.Content = "Inject";
                    LauncherButton.IsEnabled = true;
                    break;
                case LauncherStatus.WAITING_TO_INJECT:
                    LauncherButton.Content = "Injecting...";
                    LauncherButton.IsEnabled = false;
                    break;
                case LauncherStatus.INJECTED:
                    LauncherButton.Content = "Injected";
                    LauncherButton.IsEnabled = false;
                    break;
            }
        }

        private void BeginUpdate()
        {
            if (Status == LauncherStatus.UPDATE_IN_PROGRESS) return;

            // If the user has ubermenu configured, backup their Ubermenu config so it doesn't get overwritten
            try
            {
                if (TAModsUpdater.ConfigUsesUbermenu())
                {
                    TAModsUpdater.BackupUbermenuConfig();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to backup Ubermenu config: " + ex.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Run the update asynchronously
            TAModsUpdater.PerformUpdate().FireAndForget((ex) =>
            {
                MessageBox.Show("Failed to complete update: " + ex.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });

            SetStatus(LauncherStatus.UPDATE_IN_PROGRESS);
        }

        private void LaunchGame()
        {
            Config config = (Config)DataContext;

            string loginServerHost = config.LoginServer.CustomLoginServerHost;
            if (config.LoginServer.LoginServer == LoginServerMode.HiRez)
            {
                loginServerHost = TAModsNews.HirezLoginServerHost;
            }
            else if (config.LoginServer.LoginServer == LoginServerMode.Community)
            {
                loginServerHost = TAModsNews.CommunityLoginServerHost;
            }

            try
            {
                lastLaunchedProcessId = InjectorLauncher.LaunchGame(config.GamePath, loginServerHost, config.CustomArguments);
            } catch (InjectorLauncher.LauncherException ex)
            {
                MessageBox.Show("Failed to launch game: " + ex.Message, "Game Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Set up polling for process start if we're doing it by ID
            if (config.Injection.InjectByProcessId)
            {
                TALauncher.SetTarget(lastLaunchedProcessId);
            }
        }

        private void Inject()
        {
            if (Status != LauncherStatus.READY_TO_INJECT && Status != LauncherStatus.WAITING_TO_INJECT) return;

            var config = (Config)DataContext;

            string dllPath;
            switch (config.DLL.Channel)
            {
                case DLLMode.Release:
                    dllPath = "tamods.dll";
                    break;
                case DLLMode.Beta:
                    dllPath = "tamods-beta.dll";
                    break;
                case DLLMode.Edge:
                    dllPath = "tamods-edge.dll";
                    break;
                default:
                    dllPath = config.DLL.CustomDLLPath;
                    break;
            }
            
            try
            {
                InjectorLauncher.Inject(TALauncher.FoundProcess.Id, dllPath);
            } catch (InjectorLauncher.InjectorException ex)
            {
                MessageBox.Show("Failed to inject TAMods: " + ex.Message, "Injection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SetStatus(LauncherStatus.INJECTED);

            System.Media.SoundPlayer s = new System.Media.SoundPlayer(TribesLauncherSharp.Properties.Resources.blueplate);
            s.Play();
        }
        #endregion

        #region Event Handlers
        private void OnProcessLaunched(object sender, InjectorLauncher.OnProcessStatusEventArgs e)
        {
            Dispatcher.Invoke(new ThreadStart(() =>
            {
                Config config = DataContext as Config;
                // If the config is set to only consider injection by process ID, only change state if we launched it
                if (config.Injection.InjectByProcessId && e.ProcessId != lastLaunchedProcessId) return;

                if (config.Injection.IsAutomatic)
                {
                    SetStatus(LauncherStatus.WAITING_TO_INJECT);
                    AutoInjectTimer.Interval = config.Injection.AutoInjectTimer * 1000;
                    AutoInjectTimer.Start();
                } else
                {
                    SetStatus(LauncherStatus.READY_TO_INJECT);
                }
            }));
        }

        private void OnProcessEnded(object sender, InjectorLauncher.OnProcessStatusEventArgs e)
        {
            Dispatcher.Invoke(new ThreadStart(() =>
            {
                if (((Config)DataContext).Injection.InjectByProcessId)
                {
                    // Stop polling for the dead process
                    TALauncher.UnsetTarget();
                }

                SetStatus(LauncherStatus.READY_TO_LAUNCH);
            }));
        }

        private void OnProcessPollingException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Failed to poll for game launch: " + (e.ExceptionObject as Exception).Message, "Process Polling Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnAutoInjectTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Dispatcher.Invoke(new ThreadStart(() => Inject()));
        }

        private void OnUpdateFinished(object sender, EventArgs e)
        {
            // If necessary, restore the Ubermenu config backup
            Dispatcher.Invoke(new ThreadStart(() => {
                try
                {
                    if (TAModsUpdater.ConfigUsesUbermenu())
                    {
                        TAModsUpdater.RestoreUbermenuConfig();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to restore Ubermenu config: " + ex.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                SetStatus(LauncherStatus.READY_TO_LAUNCH);
            }));
        }

        private void OnUpdateProgressTick(object sender, Updater.OnProgressTickEventArgs e)
        {
            if (Math.Abs(UpdateProgressBar.Value - 100 * e.Proportion) > 5)
            {
                UpdateProgressBar.Value = 100 * e.Proportion;
            }
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

            // Download news
            try
            {
                TAModsNews.DownloadNews($"{((Config)DataContext).UpdateUrl}/news.json");
            } catch (Exception ex)
            {
                MessageBox.Show("Failed to download server information: " + ex.Message, "News Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Set up polling for game process if we're doing it by name
            if (!((Config)DataContext).Injection.InjectByProcessId)
            {
                TALauncher.SetTarget(((Config)DataContext).Injection.RunningProcessName);
            }

            // Prompt to update if need be
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var newsVersion = Version.Parse(TAModsNews.LatestLauncherVersion);
            if (newsVersion > currentVersion)
            {
                var doGoToUpdate = MessageBox.Show(
                    $"A launcher update is available. You have version {currentVersion.ToString()}, and version {newsVersion.ToString()} is available. Open update page?",
                    "Launcher Update Available", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
                switch (doGoToUpdate)
                {
                    case MessageBoxResult.Yes:
                        System.Diagnostics.Process.Start(TAModsNews.LauncherUpdateLink);
                        break;
                    default:
                        break;
                }
            }

            // Prompt to set up Ubermenu if need be
            if (((Config)DataContext).PromptForUbermenu && !TAModsUpdater.ConfigUsesUbermenu())
            {
                var doSetUp = MessageBox.Show(
                    "You have not configured Ubermenu, which allows you to configure TAMods in-game by pressing F1. Do you want to set it up now?", 
                    "Ubermenu Configuration", MessageBoxButton.YesNo, MessageBoxImage.Question);

                switch (doSetUp)
                {
                    case MessageBoxResult.Yes:
                        try
                        {
                            TAModsUpdater.SetupUbermenuPreset();
                        } catch (Exception ex)
                        {
                            MessageBox.Show("Failed to enable Ubermenu: " + ex.Message, "Ubermenu Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        break;
                    default:
                        break;
                }
            }

            // Check for an update
            if (TAModsUpdater.IsUpdateRequired())
            {
                SetStatus(LauncherStatus.UPDATE_REQUIRED);
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

        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            MessageBox.Show(
                $"TribesLauncherSharp {version}\n\n" +
                $"Application developed by mcoot. Please report bugs via Reddit (/u/avianistheterm) or Discord (mcoot#7419).\n\n" +
                $"Information about TAMods can be found at tamods.org and the TAServer GitHub: https://github.com/Griffon26/taserver/",
                "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    #endregion
}
