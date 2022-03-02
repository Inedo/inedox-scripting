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

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    [DisplayName("Execute Shell Script")]
    [Description("Executes a specified shell script.")]
    [ScriptAlias("SHExec")]
    [ScriptAlias("Execute-Shell")]
    [ScriptNamespace(Namespaces.Linux, PreferUnqualified = true)]
    [DefaultProperty(nameof(ScriptText))]
    public sealed class SHExecuteOperation : ExecuteOperation, IShellOperation
    {
        [Required]
        [ScriptAlias("Text")]
        [Description("The shell script text.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        public string ScriptText { get; set; }


        [ScriptAlias("Variables")]
        public IReadOnlyDictionary<string, RuntimeValue> InputVariables { get; set; }
        [ScriptAlias("OutVariables")]
        public IEnumerable<string> OutputVariables { get; set; }

        [ScriptAlias("Arguments")]
        [DisplayName("Command line arguments")]
        public string Arguments { get; set; }
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

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var result = await this.ExecuteShellScriptAsync(
               new ShellStartInfo
               {
                   ScriptText = this.ScriptText,
                   InjectedVariables = this.InputVariables,
                   OutVariables = this.OutputVariables?.ToList(),
                   CommandLineArguments = this.Arguments,
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
                    "as shell script"
                )
            );
        }
    }
}
