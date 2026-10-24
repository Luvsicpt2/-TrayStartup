using System.Runtime.Serialization;

namespace TrayStartup
{
    [DataContract]
    internal sealed class AppEntry
    {
        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; }

        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; }

        [DataMember(Name = "path", Order = 3)]
        public string ExePath { get; set; }

        [DataMember(Name = "arguments", Order = 4, EmitDefaultValue = false)]
        public string Arguments { get; set; }

        [DataMember(Name = "registryName", Order = 5)]
        public string RegistryName { get; set; }

        [DataMember(Name = "adopted", Order = 6, EmitDefaultValue = false)]
        public bool Adopted { get; set; }

        [DataMember(Name = "otherStartup", Order = 7, EmitDefaultValue = false)]
        public string OtherStartup { get; set; }

        public bool HasOtherStartup
        {
            get { return !string.IsNullOrWhiteSpace(OtherStartup); }
        }
    }

    [DataContract]
    internal sealed class AppConfig
    {
        [DataMember(Name = "version", Order = 1)]
        public int Version { get; set; } = 1;

        [DataMember(Name = "apps", Order = 2)]
        public System.Collections.Generic.List<AppEntry> Apps { get; set; } =
            new System.Collections.Generic.List<AppEntry>();
    }
}
