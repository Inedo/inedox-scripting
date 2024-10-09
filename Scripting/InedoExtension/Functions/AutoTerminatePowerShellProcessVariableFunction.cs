using Inedo.Documentation;
using Inedo.Extensibility;
using System.ComponentModel;
using Inedo.Extensibility.VariableFunctions;

#nullable enable

namespace Inedo.Extensions.Scripting.Functions;

[Tag("powershell")]
[ScriptAlias("AutoTerminatePowerShellProcess")]
[Description("When true, external processes created to host Windows PowerShell 5.1 scripts will be forcibly terminated after running a script to ensure resources are released. When false, processes may remain active according the PowerShell Core interop implementation.")]
[ExtensionConfigurationVariable(Required = false)]
public sealed class AutoTerminatePowerShellProcessVariableFunction : ScalarVariableFunction
{
    protected override object EvaluateScalar(IVariableFunctionContext context) => "true";
}