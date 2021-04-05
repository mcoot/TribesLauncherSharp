using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TribesLauncherSharp
{
    class LoginServer
    {
        public string Name { get; private set; }
        public string Address { get; private set; }
        public bool Default { get; private set; }
    }

    class News
    {
        public SemanticVersion LatestLauncherVersion { get; private set; }
        public string LauncherUpdateLink { get; private set; }
        public List<LoginServer> LoginServers { get; private set; }

        private static IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .IgnoreUnmatchedProperties()
            .Build();

        public static News DownloadNews()
        {
            string rawData;
            try
            {
                rawData = RemoteObjectManager.DownloadObjectAsString("news.yaml");
            }
                catch (Exception ex)
            {
                throw new NewsParsingException($"Failed to download update news data: {ex.Message}", ex);
            }

            try
            {
                return deserializer.Deserialize<News>(rawData);
            }
            catch (Exception ex)
            {
                throw new NewsParsingException($"Failed to parse update news data: {ex.Message}", ex);
            }
        }
    }

    public class NewsParsingException : Exception
    {
        public NewsParsingException() : base() { }
        public NewsParsingException(string message) : base(message) { }
        public NewsParsingException(string message, Exception inner) : base(message, inner) { }
    }
}
