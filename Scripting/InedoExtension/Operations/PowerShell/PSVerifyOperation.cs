using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.PowerShell;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    [DisplayName("PSVerify")]
    [Description("Uses a PowerShell script to collect configuration about a server.")]
    [ScriptAlias("PSVerify")]
    [Tag(Tags.PowerShell)]
    [ScriptNamespace(Namespaces.PowerShell, PreferUnqualified = true)]
    [Note("The Key is a unique string per server, and having multiple operations attempt to use the same key will yield in unpredictable behavior.")]
    public sealed class PSVerifyOperation : VerifyOperation
    {
        private PSProgressEventArgs currentProgress;

        [Required]
        [ScriptAlias("Key")]
        [DisplayName("Configuration key")]
        public string ConfigurationKey { get; set; }
        [Required]
        [ScriptAlias("Value")]
        [DisplayName("Expected value")]
        public string ExpectedValue { get; set; }
        [ScriptAlias("Collect")]
        [DisplayName("Collection script")]
        [Description("The output of this PowerShell script will be used to collect the current configuration of the server.")]
        [Category("Collect")]
        [DisableVariableExpansion]
        public string CollectScript { get; set; }
        [ScriptAlias("CollectScript")]
        [DisplayName("Collection script asset")]
        [Description("The name of a PowerShell script asset to use for collection. The output of this PowerShell script will be used to collect the current configuration of the server.")]
        [Category("Collect")]
        public string CollectScriptAsset { get; set; }
        [ScriptAlias("UseExitCode")]
        [DisplayName("Use exit code")]
        [Description("When set, the exit/return code of the script will be used instead of the output stream for collection.")]
        [Category("Collect")]
        public bool UseExitCode { get; set; }
        [ScriptAlias("Debug")]
        [Description("Captures the PowerShell Write-Debug stream into Otter's execution debug log.")]
        [Category("Logging")]
        public bool DebugLogging { get; set; }
        [ScriptAlias("Verbose")]
        [Description("Captures the PowerShell Write-Verbose stream into Otter's execution debug log.")]
        [Category("Logging")]
        public bool VerboseLogging { get; set; }
        [ScriptAlias("CollectScriptParams")]
        [DisplayName("Collection script parameters")]
        [Description("Map containing named arguments to pass to the PowerShell collect script.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [Category("Collect")]
        public IReadOnlyDictionary<string, RuntimeValue> CollectScriptParams { get; set; }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config[nameof(this.CollectScript)]))
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Verify ",
                        new Hilite(config[nameof(this.CollectScript)])
                    ),
                    new RichDescription("using PowerShell")
                );
            }
            else if (!string.IsNullOrWhiteSpace(config[nameof(this.CollectScriptAsset)]))
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Verify ",
                        new Hilite(config[nameof(this.CollectScriptAsset)])
                    ),
                    new RichDescription("using PowerShell script asset")
                );
            }
            else
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Verify ",
                        new Hilite("PowerShell")
                    )
                );
            }
        }

        public override async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context)
        {
            if (!this.ValidateConfiguration())
                return null;

            var result = await PSUtil.ExecuteScriptAsync(
                logger: this,
                context: context,
                scriptNameOrContent: AH.CoalesceString(this.CollectScriptAsset, this.CollectScript),
                scriptIsAsset: !string.IsNullOrWhiteSpace(this.CollectScriptAsset),
                arguments: this.CollectScriptParams ?? new Dictionary<string, RuntimeValue>(),
                outArguments: new Dictionary<string, RuntimeValue>(),
                collectOutput: !this.UseExitCode,
                progressUpdateHandler: (s, e) => Interlocked.Exchange(ref this.currentProgress, e)
            );

            return new KeyValueConfiguration
            {
                Key = this.ConfigurationKey,
                Value = this.UseExitCode ? result.ExitCode?.ToString() : string.Join(", ", result.Output)
            };
        }

        public override PersistedConfiguration GetConfigurationTemplate()
        {
            return new KeyValueConfiguration
            {
                Key = this.ConfigurationKey,
                Value = this.ExpectedValue
            };
        }

        public override OperationProgress GetProgress()
        {
            var p = this.currentProgress;
            return new OperationProgress(p?.PercentComplete, p?.Activity);
        }

        private bool ValidateConfiguration()
        {
            bool valid = true;

            if (string.IsNullOrWhiteSpace(this.CollectScript) && string.IsNullOrWhiteSpace(this.CollectScriptAsset))
            {
                this.LogError("Collect script missing. Specify a value for either \"Collect\" or \"CollectScript\".");
                valid = false;
            }

            if (!string.IsNullOrWhiteSpace(this.CollectScript) && !string.IsNullOrWhiteSpace(this.CollectScriptAsset))
            {
                this.LogError("Values are specified for both \"Collect\" and \"CollectScript\". Specify only one of each.");
                valid = false;
            }

            return valid;
        }
    }
}
