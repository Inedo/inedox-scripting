using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages.Python;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.Python
{
    [DisplayName("PYEnsure")]
    [ScriptAlias("PYEnsure")]
    [DefaultProperty(nameof(ScriptName))]
    [ScriptNamespace("Python", PreferUnqualified = true)]
    [Description("Uses a Python script asset to Collect, and then Ensure a configuration about a server.")]
    public sealed class PYEnsureOperation : EnsureOperation, IPythonOperation, IScriptingOperation
    {
        private PYPersistedConfiguration collectedConfiguration;

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
        [ScriptAlias("SuccessExitCode")]
        [DisplayName("Success exit code")]
        [Description("Integer exit code which indicates no error. When not specified, the exit code is ignored. This can also be an integer prefixed with an inequality operator.")]
        [Example("SuccessExitCode: 0 # Fail on nonzero.")]
        [Example("SuccessExitCode: >= 0 # Fail on negative numbers.")]
        [DefaultValue("ignored")]
        public string SuccessExitCode { get; set; }
        [Category("Advanced")]
        [ScriptAlias("PythonPath")]
        [DefaultValue("$PythonPath")]
        [DisplayName("Python path")]
        [Description("Full path to python/python.exe on the target server.")]
        public string PythonPath { get; set; }
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
        [ScriptAlias("EnvironmentVariables")]
        [DisplayName("Environment variables")]
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; }

        ScriptLanguageInfo IScriptingOperation.ScriptLanguage => new PythonScriptLanguage();

        public override async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context)
        {
            var result = await this.ExecutePythonScriptAhAsync(context, "Collect");
            if (context.Simulation)
                return null;

            this.collectedConfiguration = new PYPersistedConfiguration(result);
            return this.collectedConfiguration;
        }
        public override PersistedConfiguration GetConfigurationTemplate() => this.collectedConfiguration;
        public override Task StoreConfigurationStatusAsync(PersistedConfiguration actual, ComparisonResult results, ConfigurationPersistenceContext context)
        {
            return this.collectedConfiguration.StoreConfigurationStatusAsync(context);
        }
        public override async Task ConfigureAsync(IOperationExecutionContext context)
        {
            var result = await this.ExecutePythonScriptAhAsync(context, "Configure");
            if (context.Simulation)
                return;

            this.collectedConfiguration = new PYPersistedConfiguration(result);
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
                    new RichDescription("using Python")
                );
            }
            else
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
                        new Hilite("Python")
                    )
                );
            }
        }
    }
}
