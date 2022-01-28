using System.ComponentModel;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.Scripting.Functions
{
    [Tag("python")]
    [ScriptAlias("PythonPath")]
    [Description("The path to python/python.exe.")]
    [ExtensionConfigurationVariable(Required = false)]
    public sealed class PythonPathVariableFunction : ScalarVariableFunction
    {
        protected override object EvaluateScalar(IVariableFunctionContext context) => string.Empty;
    }
}
