using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages.Shell;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    [Tag("shell")]
    [DisplayName("SHEnsure2")]
    [ScriptAlias("SHEnsure2")]
    [DefaultProperty(nameof(ScriptName))]
    [ScriptNamespace(Namespaces.Linux, PreferUnqualified = true)]
    [Description("Uses a Shell script to collect, and then Ensure a configuration about a server.")]
    public sealed class SHEnsure2Operation : EnsureOperation, IShellOperation, IScriptingOperation
    {
        private SHPersistedConfiguration collectedConfiguration;

        [Required]
        [ScriptAlias("Name")]
        [DisplayName("Name")]
        [Description("The name of the script asset.")]
        [SuggestableValue(typeof(ScriptNameSuggestionProvider))]
        public string ScriptName { get; set; }
        [ScriptAlias("Parameters")]
        public IReadOnlyDictionary<string, RuntimeValue> Parameters { get; set; }
        [ScriptAlias("Verbose")]
        [Description("When true, additional information about staging the script is written to the debug log.")]
        public bool Verbose { get; set; }
        [Category("Logging")]
        [ScriptAlias("OutputLogLevel")]
        [DisplayName("Output log level")]
        public MessageLevel OutputLevel { get; set; } = MessageLevel.Information;
        [Category("Logging")]
        [ScriptAlias("ErrorOutputLogLevel")]
        [DisplayName("Error log level")]
        public MessageLevel ErrorLevel { get; set; } = MessageLevel.Error;
        [ScriptAlias("SuccessExitCode")]
        [DisplayName("Success exit code")]
        [Description("Integer exit code which indicates no error. When not specified, the exit code is ignored. This can also be an integer prefixed with an inequality operator.")]
        [Example("SuccessExitCode: 0 # Fail on nonzero.")]
        [Example("SuccessExitCode: >= 0 # Fail on negative numbers.")]
        [DefaultValue("ignored")]
        public string SuccessExitCode { get; set; }
        [Category("Advanced")]
        [ScriptAlias("CaptureDebug")]
        [DisplayName("Capture debug messages")]
        public bool CaptureDebug { get; set; }
        [ScriptAlias("InputVariables")]
        public IReadOnlyDictionary<string, RuntimeValue> InputVariables { get; set; }
        [ScriptAlias("OutputVariables")]
        public IEnumerable<string> OutputVariables { get; set; }
        [ScriptAlias("Arguments")]
        [DisplayName("Command line arguments")]
        public string Arguments { get; set; }

        IReadOnlyDictionary<string, string> IScriptingOperation.EnvironmentVariables { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        ScriptLanguageInfo IScriptingOperation.ScriptLanguage => new ShellScriptingLanguage();

        public override async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context)
        {
            var result = await this.ExecuteShellScriptAhAsync(context, "Collect");
            if (context.Simulation)
                return null;

            this.collectedConfiguration = new SHPersistedConfiguration(result);
            return this.collectedConfiguration;
        }
        public override PersistedConfiguration GetConfigurationTemplate() => this.collectedConfiguration;
        public override Task StoreConfigurationStatusAsync(PersistedConfiguration actual, ComparisonResult results, ConfigurationPersistenceContext context)
        {
            return this.collectedConfiguration.StoreConfigurationStatusAsync(context);
        }
        public override async Task ConfigureAsync(IOperationExecutionContext context)
        {
            if (context.Simulation)
                return;

            var result = await this.ExecuteShellScriptAhAsync(context, "Configure");
            this.collectedConfiguration = new SHPersistedConfiguration(result);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config[nameof(this.ScriptName)]))
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
                        new Hilite(config[nameof(this.ScriptName)])
                    ),
                    new RichDescription("using Shell")
                );
            }
            else
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
                        new Hilite("Shell")
                    )
                );
            }
        }
    }
}
