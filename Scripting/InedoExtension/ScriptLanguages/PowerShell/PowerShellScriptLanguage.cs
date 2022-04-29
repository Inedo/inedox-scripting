using System;
using System.ComponentModel;
using System.Linq;
using Inedo.ExecutionEngine;
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

        public override ActionStatement GetActionStatement(ScriptStatementInfo scriptStatementInfo, ActionStatement originalStatement = null)
        {
            string name;

            if (scriptStatementInfo.OperationType == typeof(PSCall2Operation))
                name = "PSCall2";
            else if (scriptStatementInfo.OperationType == typeof(PSEnsureOperation))
                name = "PSEnsure2";
            else if (scriptStatementInfo.OperationType == typeof(PSVerifyOperation))
                name = "PSVerify2";
            else
                throw new ArgumentException($"{scriptStatementInfo.OperationType} is not a supported PowerShell operation.");

            return new ActionStatement(new QualifiedName(name), scriptStatementInfo.Parameters.ToDictionary(p => p.Key, p => p.Value), new[] { scriptStatementInfo.ScriptName });
        }
        public override ScriptStatementInfo ReadActionStatement(ActionStatement actionStatement)
        {
            Type type;

            if (actionStatement.ActionName.Name.Equals("PSCall2", StringComparison.OrdinalIgnoreCase))
                type = typeof(PSCall2Operation);
            else if (actionStatement.ActionName.Name.Equals("PSEnsure2", StringComparison.OrdinalIgnoreCase))
                type = typeof(PSEnsure2Operation);
            else if (actionStatement.ActionName.Name.Equals("PSVerify2", StringComparison.OrdinalIgnoreCase))
                type = typeof(PSVerify2Operation);
            else
                throw new ArgumentException($"{actionStatement.ActionName} is not a supported PowerShell operation.");

            var scriptNameAlias = type.GetProperty(nameof(IPSScriptingOperation.ScriptName)).GetCustomAttribute<ScriptAliasAttribute>().Alias;

            var scriptName = actionStatement.Arguments.ContainsValue(scriptNameAlias) ? actionStatement.Arguments[scriptNameAlias] : actionStatement.PositionalArguments.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(scriptName))
                throw new ArgumentException($"{actionStatement.ActionName} statement is missing script name.");

            return new ScriptStatementInfo(
                scriptName: scriptName,
                parameters: actionStatement.Arguments,
                operationType: type
            );
        }

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
                Optional = p.DefaultValue != null,
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
