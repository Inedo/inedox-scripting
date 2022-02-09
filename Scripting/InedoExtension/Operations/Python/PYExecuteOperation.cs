using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Web;
using Newtonsoft.Json;

namespace Inedo.Extensions.Scripting.Operations.Python
{
    [DisplayName("Execute Python Script")]
    [Description("Executes a specified Python script.")]
    [ScriptAlias("PYExec")]
    [ScriptNamespace("Python", PreferUnqualified = true)]
    [DefaultProperty(nameof(ScriptText))]
    public sealed class PYExecuteOperation : ExecuteOperation, IPythonOperation
    {
        [Required]
        [ScriptAlias("Script")]
        [DisplayName("Script")]
        [FieldEditMode(FieldEditMode.Multiline)]
        public string ScriptText { get; set; }
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

        [ScriptAlias("Variables")]
        public IReadOnlyDictionary<string, RuntimeValue> InputVariables { get; set; }
        [ScriptAlias("OutVariables")]
        public IEnumerable<string> OutputVariables { get; set; }

        [ScriptAlias("Arguments")]
        [DisplayName("Command line arguments")]
        public string Arguments { get; set; }
        [ScriptAlias("EnvironmentVariables")]
        [DisplayName("Environment variables")]
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; }

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

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var result = await this.ExecutePythonScriptAsync(
                new PythonStartInfo
                {
                    ScriptText = this.ScriptText,
                    InjectedVariables = this.InputVariables,
                    OutVariables = this.OutputVariables?.ToList(),
                    CommandLineArguments = this.Arguments,
                    EnvironmentVariables = this.EnvironmentVariables
                },
                context
            );

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
            return new ExtendedRichDescription(
                new RichDescription(
                    "Execute ",
                    new Hilite(config[nameof(this.ScriptText)])
                ),
                new RichDescription(
                    "as Python script"
                )
            );
        }
    }
}
