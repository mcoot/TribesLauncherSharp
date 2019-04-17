using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TribesLauncherSharp
{
    class Config : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

            public enum LoginServerMode
            {
                Community,
                HiRez,
                Custom,
            }
            public LoginServerMode LoginServer { get; set; } = LoginServerMode.Community;
            public string CustomLoginServerHost { get; set; } = "127.0.0.1";
        }

        public class DLLConfig : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName)
            {
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            public enum DLLMode
            {
                Release,
                Beta,
                Edge,
                Custom
            }
            public DLLMode Channel { get; set; } = DLLMode.Release;
            public string CustomDLLPath { get; set; } = "";
        }

        public class InjectConfig : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName)
            {
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            public enum InjectMode
            {
                Manual,
                Automatic
            }
            public InjectMode Mode { get; set; } = InjectMode.Manual;
            public int AutoInjectTimer { get; set; } = 20;
        }
        

        public string GamePath { get; set; } = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Tribes\\Binaries\\Win32\\TribesAscend.exe";
        public DLLConfig DLL { get; set; } = new DLLConfig();
        public InjectConfig Injection { get; set; } = new InjectConfig();
        public LoginServerConfig LoginServer { get; set; } = new LoginServerConfig();
        public string UpdateUrl { get; set; } = "https://raw.githubusercontent.com/mcoot/tamodsupdate/release";

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
