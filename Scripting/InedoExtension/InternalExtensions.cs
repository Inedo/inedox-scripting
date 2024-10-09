using System.Linq;
using System.Reflection;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.VariableFunctions;

#nullable enable

namespace Inedo.Extensions.Scripting;

internal static class InternalExtensions
{
    public static bool GetFlagOrDefault<TFunction>(this IOperationExecutionContext context, string? value = null, bool defaultValue = false) where TFunction : VariableFunction
    {
        if (string.IsNullOrEmpty(value))
        {
            var scriptAlias = typeof(TFunction).GetCustomAttributes<ScriptAliasAttribute>().FirstOrDefault()?.Alias;
            if (!string.IsNullOrEmpty(scriptAlias))
                value = context.ExpandVariablesAsync($"${scriptAlias}").AsTask().GetAwaiter().GetResult().AsString();
        }

        return bool.TryParse(value, out var b) ? b : defaultValue;
    }
}
