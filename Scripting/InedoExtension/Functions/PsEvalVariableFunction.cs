using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.VariableFunctions;
using Inedo.Extensions.Scripting.PowerShell;

#nullable enable

namespace Inedo.Extensions.Scripting.Functions;

[ScriptAlias("PSEval")]
[Description("Returns the result of a PowerShell script.")]
[Tag("PowerShell")]
[Example("""
    # set the $NextYear variable to the value of... next year
    set $PowershellScript = >>
    (Get-Date).year + 1
    >>;

    set $NextYear = $PSEval($PowershellScript);

    Log-Information $NextYear;
    """)]
[Category("PowerShell")]
public sealed class PSEvalVariableFunction : VariableFunction, IAsyncVariableFunction
{
    [DisplayName("script")]
    [VariableFunctionParameter(0)]
    [Description("The PowerShell script to execute. This should be an expression.")]
    public string? ScriptText { get; set; }

    public override RuntimeValue Evaluate(IVariableFunctionContext context) => throw new NotImplementedException();

    public async ValueTask<RuntimeValue> EvaluateAsync(IVariableFunctionContext context)
    {
        if (context is not IOperationExecutionContext execContext)
            throw new NotSupportedException("This function can currently only be used within an execution.");

        var job = new ExecutePowerShellJob
        {
            CollectOutput = true,
            ScriptText = this.ScriptText,
            Variables = PowerShellScriptRunner.ExtractVariables(this.ScriptText, execContext),
            PreferWindowsPowerShell = execContext.GetFlagOrDefault<PreferWindowsPowerShellVariableFunction>(defaultValue: true),
            TerminateHostProcess = execContext.GetFlagOrDefault<AutoTerminatePowerShellProcessVariableFunction>(defaultValue: true)
        };

        var jobExecuter = await execContext.Agent.GetServiceAsync<IRemoteJobExecuter>().ConfigureAwait(false);

        bool errorLogged = false;

        job.MessageLogged += (s, e) =>
        {
            execContext.Log.Log(e);
            errorLogged |= e.Level == MessageLevel.Error;
        };

        var result = (ExecutePowerShellJob.Result?)await jobExecuter.ExecuteJobAsync(job, execContext.CancellationToken).ConfigureAwait(false);

        if (errorLogged)
            throw new ExecutionFailureException("PSEVal: PowerShell script failed with an error (see previous log messages).");

        return result?.Output.Count switch
        {
            null or 0 => string.Empty,
            1 => result.Output[0],
            _ => new RuntimeValue(result.Output)
        };
    }
}
