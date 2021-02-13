using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Mapping;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.PowerShell;
using Inedo.Web.Editors.Operations;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    [DisplayName("PSEnsure")]
    [Description("Calls a PowerShell Ensure Script that is stored as an asset.")]
    [ScriptAlias("PSEnsure")]
    [Tag("powershell")]
    [ScriptNamespace("PowerShell", PreferUnqualified = true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [UsesCallScriptEditor(typeof(PowerShellScriptInfoProvider))]
    [Example(@"
# execute the hdars.ps1 ensure script, passing Argument1 and Aaaaaarg2 as variables, and capturing the value of OutputArg as $MyVariable
PSEnsure hdars (
  Argument1: hello,
  Aaaaaarg2: World,
  OutputArg => $MyVariable
);
")]
    public sealed class PSEnsureOperation : EnsureOperation, ICustomArgumentMapper
    {
        private volatile PSProgressEventArgs currentProgress;

        public RuntimeValue DefaultArgument { get; set; }
        public IReadOnlyDictionary<string, RuntimeValue> NamedArguments { get; set; }
        public IDictionary<string, RuntimeValue> OutArguments { get; set; }
        
        private PSPersistedConfiguration collectedConfiguration;

        public override async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context)
        {
            var scriptName = this.DefaultArgument.AsString();
            if (string.IsNullOrWhiteSpace(scriptName))
            {
                this.LogError("Bad or missing script name.");
                return null;
            }

            if (context.Simulation)
            {
                this.LogInformation("Executing PowerShell Script...");
                return null;
            }

            var result = await PSUtil.ExecuteScriptAssetAsync(
                logger: this,
                context: context,
                fullScriptName: scriptName,
                arguments: this.NamedArguments,
                outArguments: this.OutArguments,
                collectOutput: false,
                progressUpdateHandler: (s, e) => this.currentProgress = e,
                executionMode: PsExecutionMode.Collect
            );

            this.collectedConfiguration = new PSPersistedConfiguration(result);
            return this.collectedConfiguration;
        }
        public override PersistedConfiguration GetConfigurationTemplate() => this.collectedConfiguration;
        public override Task StoreConfigurationStatusAsync(PersistedConfiguration actual, ComparisonResult results, ConfigurationPersistenceContext context)
            => this.collectedConfiguration.StoreConfigurationStatusAsync(context);

        public override async Task ConfigureAsync(IOperationExecutionContext context)
        {
            var scriptName = this.DefaultArgument.AsString();
            if (string.IsNullOrWhiteSpace(scriptName))
            {
                this.LogError("Bad or missing script name.");
                return;
            }

            if (context.Simulation)
            {
                this.LogInformation("Executing PowerShell Script...");
                return;
            }

            _ = await PSUtil.ExecuteScriptAssetAsync(
                logger: this,
                context: context,
                fullScriptName: scriptName,
                arguments: this.NamedArguments,
                outArguments: this.OutArguments,
                collectOutput: true,
                progressUpdateHandler: (s, e) => this.currentProgress = e,
                executionMode: PsExecutionMode.Configure
            );
        }
        public override OperationProgress GetProgress()
        {
            var p = this.currentProgress;
            return new OperationProgress(p?.PercentComplete, p?.Activity);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.DefaultArgument))
                return new ExtendedRichDescription(new RichDescription("PSEnsure {error parsing statement}"));

            var defaultArg = config.DefaultArgument;
            var longDesc = new RichDescription();

            bool longDescInclused = false;
            var scriptName = LooselyQualifiedName.TryParse(defaultArg);
            if (scriptName != null)
            {
                var info = PowerShellScriptInfo.TryLoad(scriptName);
                if (!string.IsNullOrEmpty(info?.Description))
                {
                    longDesc.AppendContent(info.Description);
                    longDescInclused = true;
                }

                var listParams = new List<string>();
                foreach (var prop in config.NamedArguments)
                    listParams.Add($"{prop.Key}: {prop.Value}");

                foreach (var prop in config.OutArguments)
                    listParams.Add($"{prop.Key} => {prop.Value}");

                if (listParams.Count > 0)
                {
                    if (longDescInclused)
                        longDesc.AppendContent(" - ");

                    longDesc.AppendContent(new ListHilite(listParams));
                    longDescInclused = true;
                }
            }

            if (!longDescInclused)
                longDesc.AppendContent("with no parameters");

            return new ExtendedRichDescription(
                new RichDescription("PSEnsure ", new Hilite(defaultArg)),
                longDesc
            );
        }
    }
}
