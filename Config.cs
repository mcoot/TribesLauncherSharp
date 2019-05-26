using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TribesLauncherSharp
{
    enum LoginServerMode
    {
        Community,
        HiRez,
        Custom,
    }

    enum DLLMode
    {
        Release,
        Beta,
        Edge,
        Custom
    }

    enum InjectMode
    {
        Manual,
        Automatic
    }

    class Config : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value)) return false;

            storage = value;
            this.OnPropertyChanged(propertyName);
            return true;
        }


        public class ConfigLoadException : Exception {
            public ConfigLoadException() : base() { }
            public ConfigLoadException(string message) : base(message) { }
            public ConfigLoadException(string message, Exception inner) : base(message, inner) { }
        }
        public class ConfigSaveException : Exception {
            public ConfigSaveException() : base() { }
            public ConfigSaveException(string message) : base(message) { }
            public ConfigSaveException(string message, Exception inner) : base(message, inner) { }
        }

        public class LoginServerConfig : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName)
            {
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
            {
                if (Equals(storage, value)) return false;

                storage = value;
                this.OnPropertyChanged(propertyName);
                return true;
            }

            private LoginServerMode loginServer = LoginServerMode.Community;
            public LoginServerMode LoginServer {
                get { return loginServer; }
                set
                {
                    if (SetProperty(ref loginServer, value))
                    {
                        this.OnPropertyChanged("IsCustom");
                    }
                }
            }

            private string customLoginServerHost = "127.0.0.1";
            public string CustomLoginServerHost {
                get { return customLoginServerHost; }
                set { SetProperty(ref customLoginServerHost, value); }
            }

            [YamlIgnore]
            public bool IsCustom
            {
                get { return LoginServer == LoginServerMode.Custom; }
            }
        }

        public class DLLConfig : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName)
            {
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
            {
                if (Equals(storage, value)) return false;

                storage = value;
                this.OnPropertyChanged(propertyName);
                return true;
            }

            private DLLMode channel = DLLMode.Release;
            public DLLMode Channel
            {
                get { return channel; }
                set {
                    if (SetProperty(ref channel, value))
                    {
                        this.OnPropertyChanged("IsCustom");
                    }
                }
            }

            private string customDLLPath = "";
            public string CustomDLLPath {
                get { return customDLLPath; }
                set { SetProperty(ref customDLLPath, value); }
            }

            [YamlIgnore]
            public bool IsCustom
            {
                get { return Channel == DLLMode.Custom; }
            }
        }

        public class InjectConfig : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName)
            {
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
            {
                if (Equals(storage, value)) return false;

                storage = value;
                this.OnPropertyChanged(propertyName);
                return true;
            }

            private InjectMode mode = InjectMode.Manual;
            public InjectMode Mode
            {
                get { return mode; }
                set
                {
                    if (SetProperty(ref mode, value))
                    {
                        this.OnPropertyChanged("IsAutomatic");
                    }
                }
            }
            private int autoInjectTimer = 20;
            public int AutoInjectTimer {
                get { return autoInjectTimer; }
                set { SetProperty(ref autoInjectTimer, value); }
            }

            private bool injectByProcessId = false;
            public bool InjectByProcessId
            {
                get { return injectByProcessId; }
                set { SetProperty(ref injectByProcessId, value); }
            }

            private string runningProcessName = "tribesascend";
            public string RunningProcessName
            {
                get { return runningProcessName; }
                set { SetProperty(ref runningProcessName, value); }
            }

            [YamlIgnore]
            public bool IsAutomatic
            {
                get { return Mode == InjectMode.Automatic; }
            }
        }
        
        private string gamePath = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Tribes\\Binaries\\Win32\\TribesAscend.exe";
        public string GamePath
        {
            get { return gamePath; }
            set { SetProperty(ref gamePath, value); }
        }
        private string customArguments = "";
        public string CustomArguments
        {
            get { return customArguments; }
            set { SetProperty(ref customArguments, value); }
        }
        
        public DLLConfig DLL { get; set; } = new DLLConfig();
        public InjectConfig Injection { get; set; } = new InjectConfig();
        public LoginServerConfig LoginServer { get; set; } = new LoginServerConfig();

        private string updateUrl = "https://raw.githubusercontent.com/mcoot/tamodsupdate/release";
        public string UpdateUrl
        {
            get { return updateUrl; }
            set { SetProperty(ref updateUrl, value); }
        }

        private bool promptForUbermenu = true;
        public bool PromptForUbermenu
        {
            get { return promptForUbermenu; }
            set { SetProperty(ref promptForUbermenu, value); }
        }

        private static ISerializer serializer = new SerializerBuilder()
            .EmitDefaults()
            .Build();
        private static IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(new PascalCaseNamingConvention())
            .Build();

        /// <summary>
        /// Save the configuration to a yaml file
        /// </summary>
        /// <param name="filename">The file to save to</param>
        /// <exception cref="ConfigSaveException" />
        public void Save(string filename)
        {
            try
            {
                var yaml = serializer.Serialize(this);
                File.WriteAllText(filename, yaml);
            } catch (Exception ex)
            {
                throw new ConfigSaveException(ex.Message, ex);
            }
        }

        /// <summary>
        /// Load the configuration from a yaml file
        /// </summary>
        /// <param name="filename">The file to load from</param>
        /// <returns>The parsed config</returns>
        /// <exception cref="ConfigLoadException" />
        public static Config Load(string filename)
        {
            try
            {
                string yaml = File.ReadAllText(filename);
                return deserializer.Deserialize<Config>(yaml);
            } catch (Exception ex)
            {
                throw new ConfigLoadException(ex.Message, ex);
            }
        }
    }
}
