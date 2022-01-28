using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
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
    public sealed class SHExecuteOperation : ExecuteOperation
    {
        [Required]
        [ScriptAlias("Text")]
        [Description("The shell script text.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        public string ScriptText { get; set; }
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

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            int exitCode = await SHUtil.ExecuteScriptAsync(context, new StringReader(this.ScriptText), null, this, this.Verbose, this.OutputLevel, this.ErrorLevel) ?? 0;

            bool exitCodeLogged = false;

            if (!string.IsNullOrWhiteSpace(this.SuccessExitCode))
            {
                var comparator = ExitCodeComparator.TryParse(this.SuccessExitCode);
                if (comparator != null)
                {
                    bool result = comparator.Evaluate(exitCode);
                    if (result)
                        this.LogInformation($"Script exited with code: {exitCode} (success)");
                    else
                        this.LogError($"Script exited with code: {exitCode} (failure)");

                    exitCodeLogged = true;
                }
            }

            if (!exitCodeLogged)
                this.LogDebug("Script exited with code: " + exitCode);
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
