﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
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
    [DisplayName("PSEnsure using Scripts")]
    [Description("Uses two PowerShell scripts to Collect, and then Ensure a configuration about a server.")]
    [ScriptAlias("PSEnsureScripts")]
    [Tag(Tags.PowerShell)]
    [ScriptNamespace(Namespaces.PowerShell, PreferUnqualified = true)]
    [Example(@"
# ensures the BuildMaster Agent service exists on the remote server
PSEnsure(
    Key: BuildMasterAgentInstalled,
    # returns the count of INEDOBMAGT services installed
    Collect: @(Get-Service | Where-Object {$_.Name -eq ""INEDOBMAGT""}).Count,
    # expected value is 1
    Value: 1,
    # if the returned value is 0 instead of 1, the installer will run
    Configure: & '\\filesrv1000\$e\Resources\BuildMasterAgentSetup.exe' /S /AgentType=TCP /Port=8080,
    Debug: true,
    Verbose: true
);

# ensures the BuildMaster Agent service exists on the remote server, using a
# PowerShell script asset to perform the configuration
PSEnsure(
    Key: BuildMasterAgentInstalled,
    # returns the count of INEDOBMAGT services installed
    Collect: @(Get-Service | Where-Object {$_.Name -eq ""INEDOBMAGT""}).Count,
    # expected value is 1
    Value: 1,
    # use script stored in InstallBmAgent asset
    ConfigureScript: InstallBmAgent,
    ConfigureScriptParams: %(
        AgentType: TCP,
        Port: 1000),
    Debug: true,
    Verbose: true
);
")]
    [Note("The Key is a unique string per server, and having multiple operations attempt to use the same key will yield in unpredictable behavior.")]
    public sealed class PSEnsureScriptsOperation : EnsureOperation
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
        [ScriptAlias("Configure")]
        [DisplayName("Configure script")]
        [Description("This PowerShell script is executed if the configuration gathered using the collection script does not match the stored configuration.")]
        [Category("Configure")]
        [DisableVariableExpansion]
        public string ConfigureScript { get; set; }
        [ScriptAlias("CollectScript")]
        [DisplayName("Collection script asset")]
        [Description("The name of a PowerShell script asset to use for collection. The output of this PowerShell script will be used to collect the current configuration of the server.")]
        [Category("Collect")]
        public string CollectScriptAsset { get; set; }
        [ScriptAlias("ConfigureScript")]
        [Category("Configure")]
        [DisplayName("Configuration script asset")]
        [Description("The name of a PowerShell script asset to use for configuration. This script is executed if the configuration gathered using the collection script does not match the stored configuration.")]
        public string ConfigureScriptAsset { get; set; }
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
        [ScriptAlias("ConfigureScriptParams")]
        [DisplayName("Configure script parameters")]
        [Description("Map containing named arguments to pass to the PowerShell configure script.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [Category("Configure")]
        public IReadOnlyDictionary<string, RuntimeValue> ConfigureScriptParams { get; set; }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config[nameof(this.CollectScript)]))
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
                        new Hilite(config[nameof(this.CollectScript)])
                    ),
                    new RichDescription("using PowerShell")
                );
            }
            else if (!string.IsNullOrWhiteSpace(config[nameof(this.CollectScriptAsset)]))
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
                        new Hilite(config[nameof(this.CollectScriptAsset)])
                    ),
                    new RichDescription("using PowerShell script asset")
                );
            }
            else
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
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

        public override async Task ConfigureAsync(IOperationExecutionContext context)
        {
            if (!this.ValidateConfiguration())
                return;

            _ = await PSUtil.ExecuteScriptAsync(
                logger: this,
                context: context,
                scriptNameOrContent: AH.CoalesceString(this.ConfigureScriptAsset, this.ConfigureScript),
                scriptIsAsset: !string.IsNullOrWhiteSpace(this.ConfigureScriptAsset),
                arguments: this.ConfigureScriptParams ?? new Dictionary<string, RuntimeValue>(),
                outArguments: new Dictionary<string, RuntimeValue>(),
                collectOutput: false,
                progressUpdateHandler: (s, e) => Interlocked.Exchange(ref this.currentProgress, e)
            );
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

            if (string.IsNullOrWhiteSpace(this.ConfigureScript) && string.IsNullOrWhiteSpace(this.ConfigureScriptAsset))
            {
                this.LogError("Configure script missing. Specify a value for either \"Configure\" or \"ConfigureScript\".");
                valid = false;
            }

            if (!string.IsNullOrWhiteSpace(this.CollectScript) && !string.IsNullOrWhiteSpace(this.CollectScriptAsset))
            {
                this.LogError("Values are specified for both \"Collect\" and \"CollectScript\". Specify only one of each.");
                valid = false;
            }

            if (!string.IsNullOrWhiteSpace(this.ConfigureScript) && !string.IsNullOrWhiteSpace(this.ConfigureScriptAsset))
            {
                this.LogError("Values are specified for both \"Configure\" and \"ConfigureScript\". Specify only one of each.");
                valid = false;
            }

            return valid;
        }
    }
}
