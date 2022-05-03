using System;
using System.ComponentModel;
using System.Linq;
using Inedo.Extensibility;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.Operations.PowerShell;
using Inedo.Extensions.Scripting.PowerShell;

namespace Inedo.Extensions.Scripting.ScriptLanguages.PowerShell
{
    [DisplayName("PowerShell")]
    [Description("Parses additional headers in PowerShell scripts.")]
    public sealed class PowerShellScriptLanguage : ScriptLanguageInfo
    {
        public override string LanguageName => "PowerShell";
        public override string FileExtension => ".ps1";
        public override ScriptParameterUsage ParameterUsage => ScriptParameterUsage.InputVariable | ScriptParameterUsage.OutputVariable;
        public override Type CallOperationType => typeof(PSCall2Operation);
        public override Type EnsureOperationType => typeof(PSEnsure2Operation);
        public override Type VerifyOperationType => typeof(PSVerify2Operation);
        public override string SyntaxName => "powershell";
        public override FileResource Icon { get; } = new EmbeddedFileResource("PowerShell.powershell.svg", "image/svg+xml");

        protected override ScriptInfo ParseScriptInfo(RaftItem2 script)
        {
            using var source = script.OpenTextReader();
            if (!PowerShellScriptInfo.TryParse(source, out var info))
                return null;

            return new ScriptInfo(
                parameters: info.Parameters.Select(Convert).ToList(),
                summary: info.Description,
                configVariables: info.ConfigParameters?.Select(Convert)?.ToList(),
                ahExecModeVariable: info.ExecutionModeVariableName
            );
        }

        private static ScriptParameterInfo Convert(PowerShellParameterInfo p)
        {
            return new ScriptParameterInfo
            {
                Name = p.Name,
                DefaultValue = p.DefaultValue,
                Description = p.Description,
                Optional = !p.Mandatory,
                Type = p.IsBooleanOrSwitch ? ScriptParameterType.Bool : ScriptParameterType.Text,
                Usage = p.IsOutput ? ScriptParameterUsage.OutputVariable : ScriptParameterUsage.InputVariable
            };
        }
        private static ScriptConfigurationValues Convert(PSConfigParameterInfo p)
        {
            return new ScriptConfigurationValues
            {
                ConfigKey = p.ConfigKey,
                ConfigType = p.ConfigType,
                CurrentValue = p.CurrentValue,
                DesiredValue = p.DesiredValue,
                ValueDrifted = p.ValueDrifted
            };
        }
    }
}
