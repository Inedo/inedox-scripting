using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;

using Inedo.Extensions.Scripting.Configurations.PsModule;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    [Tag(Tags.PowerShell)]
    [DisplayName("Ensure PowerShell Module")]
    [Description("Ensures that the specified PowerShell module is installed.")]
    [ScriptAlias("Ensure-PsModule")]
    [ScriptNamespace(Namespaces.PowerShell, PreferUnqualified = true)]
    [Note("An argument may be explicitly converted to an integral type by prefixing the value with [type::<typeName>], where <typeName> is one of: int, uint, long, ulong, double, decimal. Normally this conversion is performed automatically and this is not necessary.")]
    [Example(@"
# ensures the existence of a file on the server
Ensure-DscResource(
  Name: File,
  ConfigurationKey: DestinationPath,
  Properties: %(
    DestinationPath: C:\hdars\1000.txt,
    Contents: test file ensured)
);

# runs a custom resource
Ensure-DscResource(
  Name: cHdars,
  Module: cHdarsResource,
  ConfigurationKey: LocalServer,
  Properties: %(
    MaximumSessionLength: 1000,
    PortsToListen: @(3322,4431,1123),
    Enabled: true)
);")]
    public sealed class EnsurePsModuleOperation : EnsureOperation<PsModuleConfiguration>
    {
        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config) => PsModuleConfiguration.GetDescription(config);

        public override Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context) => PsModuleConfiguration.CollectAsync(context, this, this.Template);

        public override Task ConfigureAsync(IOperationExecutionContext context) => PsModuleConfiguration.ConfigureAsync(context, this, this.Template);
    }
}
