using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.Configurations.DSC;
using Inedo.Extensions.Scripting.PowerShell;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    [DisplayName("Collect DSC Modules")]
    [Description("Collects the names and versions of DSC modules installed on a server.")]
    [ScriptAlias("Collect-DscModules")]
    [Tag(Tags.PowerShell)]
    [ScriptNamespace(Namespaces.PowerShell, PreferUnqualified = true)]
    public sealed class CollectDscModulesOperation : CollectPackagesOperation
    {
        public override string PackageType => "DSC Module";

        protected override async Task<IEnumerable<PackageConfiguration>> CollectPackagesAsync(IOperationCollectionContext context)
        {
            var preferWindowsPowerShell = bool.TrueString;
            var maybeVariable = context.TryGetVariableValue(new RuntimeVariableName("PreferWindowsPowerShell", RuntimeValueType.Scalar));
            if (maybeVariable == null)
            {
                var maybeFunc = context.TryGetFunctionValue("PreferWindowsPowerShell");
                if (maybeFunc != null)
                    preferWindowsPowerShell = maybeFunc.Value.AsString();
            }
            else
                preferWindowsPowerShell = maybeVariable.Value.AsString();

            using var job = new CollectDscModulesJob { DebugLogging = true, PreferWindowsPowerShell = bool.TryParse(preferWindowsPowerShell, out bool _preferWindowsPowerShell) ? _preferWindowsPowerShell : true };
            job.MessageLogged += (s, e) => this.Log(e.Level, e.Message);

            var jobExecuter = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();
            var result = (CollectDscModulesJob.Result)await jobExecuter.ExecuteJobAsync(job, context.CancellationToken);

            return result.Modules.Select(i => new DscModuleConfiguration { PackageName = i.Name, PackageVersion = i.Version });
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config) => new ExtendedRichDescription(new RichDescription("Collect PowerShell DSC Modules"));
    }
}
