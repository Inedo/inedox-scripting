﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.PowerShell;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;
using Inedo.Web.Editors.Operations;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    [DisplayName("PSVerify2")]
    [Description("Uses a PowerShell script to collect configuration about a server.")]
    [ScriptAlias("PSVerify2")]
    [Tag(Tags.PowerShell)]
    [UsesCallScriptEditor(typeof(PowerShellScriptInfoProvider))]
    [ScriptNamespace(Namespaces.PowerShell, PreferUnqualified = true)]
    [DefaultProperty(nameof(ScriptName))]
    public sealed class PSVerify2Operation : VerifyOperation, IPSScriptingOperation
    {
        private volatile PSProgressEventArgs currentProgress;

        ScriptLanguageInfo IScriptingOperation.ScriptLanguage => new ScriptLanguages.PowerShell.PowerShellScriptLanguage();

        [Required]
        [ScriptAlias("Name")]
        [DisplayName("Name")]
        [Description("The name of the script asset.")]
        [SuggestableValue(typeof(ScriptNameSuggestionProvider))]
        public string ScriptName { get; set; }
        [ScriptAlias("Parameters")]
        public IReadOnlyDictionary<string, RuntimeValue> Parameters { get; set; }
        [ScriptAlias("InputVariables")]
        public IReadOnlyDictionary<string, RuntimeValue> InputVariables { get; set; }
        [ScriptAlias("OutputVariables")]
        public IEnumerable<string> OutputVariables { get; set; }

        string IScriptingOperation.Arguments { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        IReadOnlyDictionary<string, string> IScriptingOperation.EnvironmentVariables { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        private PSPersistedConfiguration collectedConfiguration;

        public override async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context)
        {
            if (context.Simulation)
            {
                this.LogInformation("Executing PowerShell Script...");
                return null;
            }

            var result = await PSUtil2.ExecuteScript2Async(
                operation: this,
                context: context,
                collectOutput: true,
                progressUpdateHandler: (s, e) => this.currentProgress = e,
                executionMode: PsExecutionMode.Collect
            );

            this.collectedConfiguration = new PSPersistedConfiguration(result);
            return this.collectedConfiguration;
        }
        public override PersistedConfiguration GetConfigurationTemplate() => this.collectedConfiguration;
        public override Task StoreConfigurationStatusAsync(PersistedConfiguration actual, ComparisonResult results, ConfigurationPersistenceContext context)
            => this.collectedConfiguration.StoreConfigurationStatusAsync(context);

        public override OperationProgress GetProgress()
        {
            var p = this.currentProgress;
            return new OperationProgress(p?.PercentComplete, p?.Activity);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var scriptName = config[nameof(IPSScriptingOperation.ScriptName)].ToString() ?? config.DefaultArgument;
            if (string.IsNullOrWhiteSpace(scriptName))
                return new ExtendedRichDescription(new RichDescription("PSVerify2 {error parsing statement}"));

            var longDesc = new RichDescription();

            bool longDescInclused = false;
            var parsedScriptName = LooselyQualifiedName.TryParse(scriptName);
            if (parsedScriptName != null)
            {
                var info = PowerShellScriptInfo.TryLoad(parsedScriptName);
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
                new RichDescription("PSVerify2 ", new Hilite(scriptName)),
                longDesc
            );
        }
    }
}