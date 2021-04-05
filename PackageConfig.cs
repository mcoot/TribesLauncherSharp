using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TribesLauncherSharp
{
    class Package
    {
        // Unique identifier for the package
        public string Id { get; set; }
        // User-facing package name
        public string DisplayName { get; set; }
        // Package description
        public string Description { get; set; }
        // Version of the package; used to determine whether a package requires updates
        public SemanticVersion Version { get; set; }
        // The key of the zipped package in S3
        public string ObjectKey { get; set; }
        // Whether the package is mandatory for TAMods
        public bool Required { get; set; }
    }

    class PackageConfig
    {
        public List<Package> Packages { get; set; }
        
        private static IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .Build();

        public static PackageConfig ParsePackageConfig(string config)
        {
            try
            {
                return deserializer.Deserialize<PackageConfig>(config);
            }
            catch (Exception ex)
            {
                throw new PackageConfigLoadException(ex.Message, ex);
            }
        }

        class PackageConfigLoadException : Exception
        {
            public PackageConfigLoadException() : base() { }
            public PackageConfigLoadException(string message) : base(message) { }
            public PackageConfigLoadException(string message, Exception inner) : base(message, inner) { }
        }
    }
}
