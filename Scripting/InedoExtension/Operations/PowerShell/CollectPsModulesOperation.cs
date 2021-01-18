using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.Configurations.PsModule;
using Inedo.Extensions.Scripting.PowerShell;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    [DisplayName("Collect PowerShell Modules")]
    [Description("Collects the names and versions of PowerShell modules installed on a server.")]
    [ScriptAlias("Collect-PsModules")]
    [Tag(Tags.PowerShell)]
    [ScriptNamespace(Namespaces.PowerShell, PreferUnqualified = true)]
    public sealed class CollectPsModulesOperation : CollectPackagesOperation
    {
        public override string PackageType => "PowerShell Module";

        protected async override Task<IEnumerable<PackageConfiguration>> CollectPackagesAsync(IOperationCollectionContext context)
        {
            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();
            var scriptText = "$results = Get-Module -ListAvailable";
                
            var job = new ExecutePowerShellJob
            {
                CollectOutput = true,
                OutVariables = new[] { "results" },
                ScriptText = scriptText
            };

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(job);

            var modules = (result.OutVariables["results"].AsEnumerable() ?? result.OutVariables["results"].ParseDictionary() ?? Enumerable.Empty<RuntimeValue>())
                .Select(parseModule).Where(m => m != null);

            return modules;

            static PsModulePackageConfiguration parseModule(RuntimeValue value)
            {
                var module = value.AsDictionary();

                if (module == null)
                    return null;

                if (!module.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name.AsString()))
                    return null;

                module.TryGetValue("Version", out var version);
                module.TryGetValue("ModuleType", out var moduleType);

                return new PsModulePackageConfiguration
                {
                    PackageName = name.AsString(),
                    PackageVersion = version.AsString() ?? string.Empty,
                    ModuleType = moduleType.AsString()
                };
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription("Collect PowerShell Modules")
            );
        }
    }
}
