using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.Python
{
    [DisplayName("PYCall")]
    [Description("Calls a Python script that is stored as an asset.")]
    [ScriptAlias("PYCall")]
    [ScriptNamespace("python", PreferUnqualified = true)]
    [DefaultProperty(nameof(ScriptName))]
    public sealed class PYCallOperation : ExecuteOperation, IPythonOperation, IScriptingOperation
    {
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

        [ScriptAlias("InputVariables")]
        public IReadOnlyDictionary<string, RuntimeValue> InputVariables { get; set; }
        [ScriptAlias("OutputVariables")]
        public IEnumerable<string> OutputVariables { get; set; }

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

        [ScriptAlias("Arguments")]
        [DisplayName("Command line arguments")]
        public string Arguments { get; set; }
        [ScriptAlias("EnvironmentVariables")]
        [DisplayName("Environment variables")]
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; }

        ScriptLanguageInfo IScriptingOperation.ScriptLanguage => new PythonScriptLanguage();

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var result = await this.ExecutePythonScriptAhAsync(context, "Execute");
            if (context.Simulation)
                return;

            if (result.OutVariables != null)
            {
                foreach (var v in result.OutVariables)
                    context.SetVariableValue(new RuntimeVariableName(v.Key, v.Value.ValueType), v.Value);
            }

            bool exitCodeLogged = false;

            if (!string.IsNullOrWhiteSpace(this.SuccessExitCode))
            {
                var comparator = ExitCodeComparator.TryParse(this.SuccessExitCode);
                if (comparator != null)
                {
                    if (comparator.Evaluate(result.ExitCode.GetValueOrDefault()))
                        this.LogInformation($"Script exited with code: {result.ExitCode} (success)");
                    else
                        this.LogError($"Script exited with code: {result.ExitCode} (failure)");

                    exitCodeLogged = true;
                }
            }

            if (!exitCodeLogged)
                this.LogDebug("Script exited with code: " + result.ExitCode);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var shortDesc = new RichDescription("PYCall ", new Hilite(config[nameof(this.ScriptName)]));
            var args = config[nameof(this.InputVariables)];
            if (string.IsNullOrWhiteSpace(args))
            {
                return new ExtendedRichDescription(shortDesc);
            }
            else
            {
                return new ExtendedRichDescription(
                    shortDesc,
                    new RichDescription(
                        "with arguments ",
                        new Hilite(args)
                    )
                );
            }
        }
    }
}
