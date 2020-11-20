using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using Inedo.Agents;
using Inedo.Documentation;
using Inedo.Extensions.Scripting.Operations.Shell;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.Scripting.Functions
{
    [ScriptAlias("SHEval")]
    [Description("Returns the output of a shell script.")]
    [Tag("Linux")]
    [Example(@"
# set the $NextYear variable to the value of... next year
set $ShellScript = >>
date -d next-year +%Y
>>;
set $NextYear = $SHEval($ShellScript);
Log-Information $NextYear;
")]
    [AppliesTo(InedoProduct.BuildMaster | InedoProduct.Otter)]
    public sealed class SHEvalVariableFunction : ScalarVariableFunction
    {
        [DisplayName("script")]
        [VariableFunctionParameter(0)]
        [Description("The shell script to execute. This should be an expression.")]
        public string ScriptText { get; set; }

        protected override object EvaluateScalar(IVariableFunctionContext context)
        {
            var execContext = context as IOperationExecutionContext;
            if (execContext == null)
                throw new NotSupportedException("This function can currently only be used within an execution.");
            if (execContext.Agent.TryGetService<ILinuxFileOperationsExecuter>() == null)
                throw new NotSupportedException("This function is only valid when run against an SSH agent.");

            var output = new StringBuilder();

            SHUtil.ExecuteScriptAsync(execContext, new StringReader(this.ScriptText), null, NullLogSink.Instance, false, data => output.AppendLine(data)).GetAwaiter().GetResult();

            return output.ToString();
        }
    }
}
