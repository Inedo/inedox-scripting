using Inedo.Documentation;
using Inedo.Extensibility;
using System.ComponentModel;
using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.Scripting.Functions
{
    [Tag("powershell")]
    [ScriptAlias("PreferWindowsPowerShell")]
    [Description("When true, the script will be run using Windows PowerShell 5.1 where available. When false or on Linux (or on Windows systems without PowerShell 5.1 installed), the script will be run using PowerShell Core instead.")]
    [ExtensionConfigurationVariable(Required = false)]
    public sealed class PreferWindowsPowerShellVariableFunction : ScalarVariableFunction
    {
        protected override object EvaluateScalar(IVariableFunctionContext context) => bool.TrueString;
    }
}
