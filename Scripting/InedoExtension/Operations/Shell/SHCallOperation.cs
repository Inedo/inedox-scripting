using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    [DisplayName("SHCall")]
    [Description("Calls a shell script that is stored as an asset.")]
    [ScriptAlias("SHCall")]
    [ScriptNamespace(Namespaces.Linux, PreferUnqualified = true)]
    [DefaultProperty(nameof(ScriptName))]
    public sealed class SHCallOperation : ExecuteOperation
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
        [Category("Logging")]
        [ScriptAlias("OutputLogLevel")]
        [DisplayName("Output log level")]
        public MessageLevel OutputLevel { get; set; } = MessageLevel.Information;
        [Category("Logging")]
        [ScriptAlias("ErrorOutputLogLevel")]
        [DisplayName("Error log level")]
        public MessageLevel ErrorLevel { get; set; } = MessageLevel.Error;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            using var scriptReader = SHUtil.OpenScriptAsset(this.ScriptName, this, context);
            if (scriptReader == null)
                return;

            this.ExitCode = await SHUtil.ExecuteScriptAsync(context, scriptReader, this.Arguments, this, this.Verbose, this.OutputLevel, this.ErrorLevel).ConfigureAwait(false);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var shortDesc = new RichDescription("SHCall ", new Hilite(config[nameof(this.ScriptName)]));
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
