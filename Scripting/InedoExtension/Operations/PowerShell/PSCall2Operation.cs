using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.PowerShell;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{

    [DisplayName("PSCall2")]
    [Description("Calls a PowerShell Script that is stored as an asset.")]
    [ScriptAlias("PSCall2")]
    [Tag("powershell")]
    [ScriptNamespace("PowerShell", PreferUnqualified = true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Note("An argument may be explicitly converted to an integral type by prefixing the value with [type::<typeName>], where <typeName> is one of: int, uint, long, ulong, double, decimal. Normally this conversion is performed automatically and this is not necessary.")]
    [Example(@"
# execute the hdars.ps1 script, passing Argument1 and Aaaaaarg2 as input variables, and capturing the value of OutputArg as $OutputArg 
PSCall2 hdars.ps1 (
  InputVariables: %(Argument1: hello, Aaaaaarg2: World),
  OutputVariables: @(MyOutputArg)
);
")]
    [DefaultProperty(nameof(ScriptName))]
    public sealed class PSCall2Operation : ExecuteOperation, IPSScriptingOperation
    {
        private PSProgressEventArgs currentProgress;

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

        public override Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (context.Simulation)
            {
                this.LogInformation("Executing PowerShell Script...");
                return Complete;
            }

            var fullScriptName = this.ScriptName;
            if (fullScriptName == null || !fullScriptName.EndsWith(".ps1"))
            {
                this.LogError("Bad or missing script name.");
                return Complete;
            }

            return PSUtil2.ExecuteScript2Async(
                operation: this,
                context: context,
                collectOutput: false,
                progressUpdateHandler: (s, e) => Interlocked.Exchange(ref this.currentProgress, e)
            );
        }
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
                new RichDescription("PSCall2 ", new Hilite(scriptName)),
                longDesc
            );
        }
    }
}
