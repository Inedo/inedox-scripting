﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Mapping;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.PowerShell;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    [DisplayName("PSCall")]
    [Description("Calls a PowerShell Script that is stored as an asset.")]
    [ScriptAlias("PSCall1")]
    [Tag("powershell")]
    [ScriptNamespace("PowerShell", PreferUnqualified = true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Note("An argument may be explicitly converted to an integral type by prefixing the value with [type::<typeName>], where <typeName> is one of: int, uint, long, ulong, double, decimal. Normally this conversion is performed automatically and this is not necessary.")]
    [Example(@"
# execute the hdars.ps1 script, passing Argument1 and Aaaaaarg2 as variables, and capturing the value of the PowerShell variable MyVariableSetInPowerShell to the OtterScript variable $MyVariable
pscall hdars (
  Argument1: hello,
  Aaaaaarg2: World,
  MyVariableSetInPowerShell => $MyVariable
);
")]
    public sealed class PSCallOperation : ExecuteOperation, ICustomArgumentMapper
    {
        private PSProgressEventArgs currentProgress;

        public RuntimeValue DefaultArgument { get; set; }
        public IReadOnlyDictionary<string, RuntimeValue> NamedArguments { get; set; }
        public IDictionary<string, RuntimeValue> OutArguments { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (context.Simulation)
            {
                this.LogInformation("Executing PowerShell Script...");
                return;
            }

            var fullScriptName = this.DefaultArgument.AsString();
            if (fullScriptName == null)
            {
                this.LogError("Bad or missing script name.");
                return;
            }

            await PSUtil.ExecuteScriptAssetAsync(
                logger: this,
                context: context,
                fullScriptName: fullScriptName,
                arguments: this.NamedArguments,
                outArguments: this.OutArguments,
                collectOutput: false,
                progressUpdateHandler: (s, e) => Interlocked.Exchange(ref this.currentProgress, e),
                preferWindowsPowerShell: !bool.TryParse((await context.ExpandVariablesAsync("$PreferWindowsPowerShell")).AsString(), out bool p) || p
            );
        }
        public override OperationProgress GetProgress()
        {
            var p = this.currentProgress;
            return new OperationProgress(p?.PercentComplete, p?.Activity);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.DefaultArgument))
                return new ExtendedRichDescription(new RichDescription("PSCall {error parsing statement}"));

            var defaultArg = config.DefaultArgument;
            var longDesc = new RichDescription();

            bool longDescInclused = false;
            var scriptName = LooselyQualifiedName.TryParse(defaultArg);
            if (scriptName != null)
            {
                var info = PowerShellScriptInfo.TryLoad(scriptName);
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
                new RichDescription("PSCall ", new Hilite(defaultArg)),
                longDesc
            );
        }
    }
}
