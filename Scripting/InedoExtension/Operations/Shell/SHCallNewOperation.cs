using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages.Shell;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    [DisplayName("SHCallNew")]
    [Description("Calls a shell script that is stored as an asset.")]
    [ScriptAlias("SHCallNew")]
    [ScriptNamespace(Namespaces.Linux, PreferUnqualified = true)]
    [DefaultProperty(nameof(ScriptName))]
    public sealed class SHCallNewOperation : ExecuteOperation, IShellOperation, IScriptingOperation
    {
        [Required]
        [ScriptAlias("Name")]
        [DisplayName("Name")]
        [Description("The name of the script asset.")]
        [SuggestableValue(typeof(ScriptNameSuggestionProvider))]
        public string ScriptName { get; set; }
        [ScriptAlias("Arguments")]
        [Description("Arguments to pass to the script.")]
        public string Arguments { get; set; }
        [ScriptAlias("Verbose")]
        [Description("When true, additional information about staging the script is written to the debug log.")]
        public bool Verbose { get; set; }
        [Output]
        [ScriptAlias("ExitCode")]
        [DisplayName("Exit code")]
        [PlaceholderText("eg. $ScriptExitCode")]
        public int? ExitCode { get; set; }
        [ScriptAlias("SuccessExitCode")]
        [DisplayName("Success exit code")]
        [Description("Integer exit code which indicates no error. When not specified, the exit code is ignored. This can also be an integer prefixed with an inequality operator.")]
        [Example("SuccessExitCode: 0 # Fail on nonzero.")]
        [Example("SuccessExitCode: >= 0 # Fail on negative numbers.")]
        [DefaultValue("ignored")]
        public string SuccessExitCode { get; set; }


        [Category("Logging")]
        [ScriptAlias("OutputLogLevel")]
        [DisplayName("Output log level")]
        public MessageLevel OutputLevel { get; set; } = MessageLevel.Information;
        [Category("Logging")]
        [ScriptAlias("ErrorOutputLogLevel")]
        [DisplayName("Error log level")]
        public MessageLevel ErrorLevel { get; set; } = MessageLevel.Error;

        [ScriptAlias("Parameters")]
        public IReadOnlyDictionary<string, RuntimeValue> Parameters { get; set; }
        [ScriptAlias("InputVariables")]
        public IReadOnlyDictionary<string, RuntimeValue> InputVariables { get; set; }
        [ScriptAlias("OutputVariables")]
        public IEnumerable<string> OutputVariables { get; set; }

        [Category("Advanced")]
        [ScriptAlias("CaptureDebug")]
        [DisplayName("Capture debug messages")]
        public bool CaptureDebug { get; set; }


        ScriptLanguageInfo IScriptingOperation.ScriptLanguage => new ShellScriptingLanguage();

        IReadOnlyDictionary<string, string> IScriptingOperation.EnvironmentVariables { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (context.Simulation)
                return;
            var result = await this.ExecuteShellScriptAhAsync(context, "Execute");

            if (result.OutVariables != null)
            {
                foreach (var v in result.OutVariables)
                    context.SetVariableValue(new RuntimeVariableName(v.Key, v.Value.ValueType), v.Value);
            }

            this.ExitCode = result.ExitCode;

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
            var shortDesc = new RichDescription("SHCallNew ", new Hilite(config[nameof(this.ScriptName)]));
            var args = config[nameof(this.Arguments)];
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
