using System;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Serialization;

namespace Inedo.Extensions.Scripting.Configurations.PsModule
{
    /// <summary>
    /// Provides additional metadata for installed Debian packages.
    /// </summary>
    [Serializable]
    [SlimSerializable]
    [ScriptAlias("PowerShellModule")]
    public class PsModulePackageConfiguration : PackageConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PsModulePackageConfiguration"/> class.
        /// </summary>
        public PsModulePackageConfiguration()
        {
        }

        public override string ConfigurationKey  => $"{this.PackageName}::{this.PackageVersion}";

        /// <summary>
        /// Gets or sets the module type of the PowerShell Module.
        /// </summary>
        [Persistent]
        public string ModuleType { get; set; }

        //HelpInfoUri
    }
}
