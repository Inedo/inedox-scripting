using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.ExecutionEngine;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensions.Scripting.Functions;
using Inedo.Extensions.Scripting.Operations.PowerShell;

namespace Inedo.Extensions.Scripting.PowerShell
{
    internal static class PSUtil2
    {
        public static async Task<ExecuteScriptResult> ExecuteScript2Async(IPSScriptingOperation operation, IOperationExecutionContext context, bool collectOutput, EventHandler<PSProgressEventArgs> progressUpdateHandler, string successExitCode = null, PsExecutionMode executionMode = PsExecutionMode.Normal, bool preferWindowsPowerShell = true)
        {
            string scriptContent;
            PowerShellScriptInfo scriptInfo;

            if (string.IsNullOrEmpty(operation.ScriptText))
            {
                if (operation.ScriptName?.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) != true)
                {
                    operation.LogError($"PowerShell Script name \"{operation.ScriptName}\" is invalid.");
                    return null;
                }

                var scriptItem = SDK.GetRaftItem(RaftItemType.Script, operation.ScriptName, context);
                if (scriptItem == null)
                {
                    operation.LogError($"PowerShell Script \"{operation.ScriptName}\" not found.");
                    return null;
                }

                scriptContent = scriptItem.Content;
                if (!PowerShellScriptInfo.TryParse(new StringReader(scriptContent), out scriptInfo))
                {
                    operation.LogDebug($"PowerShell Script \"{operation.ScriptName}\" could not be parsed, and may error upon executing.");
                    scriptInfo = null;
                }
            }
            else
            {
                scriptContent = operation.ScriptText;
                if (!PowerShellScriptInfo.TryParse(new StringReader(scriptContent), out scriptInfo))
                {
                    operation.LogDebug("PowerShell Script could not be parsed, and may error upon executing.");
                    scriptInfo = null;
                }
            }

            var psVariables = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var psParameters = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var psOutVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (operation.Parameters != null)
            {
                if (scriptInfo == null)
                {
                    foreach (var param in operation.Parameters)
                        psVariables.Add(param.Key, param.Value);
                }
                else
                {
                    foreach (var param in operation.Parameters)
                    {
                        var psParamInfo = scriptInfo.Parameters.FirstOrDefault(p => string.Equals(p.Name, param.Key, StringComparison.OrdinalIgnoreCase));
                        if (psParamInfo == null)
                        {
                            psVariables.Add(param.Key, param.Value);
                            continue;
                        }

                        var paramValue = psParamInfo.IsBooleanOrSwitch ? (param.Value.AsBoolean() ?? false) : param.Value;

                        if (psParamInfo.IsOutput)
                            psOutVariables.Add(param.Key, paramValue.ToString());
                        else
                            psParameters.Add(param.Key, paramValue);
                    }
                }
            }

            if (operation.OutputVariables != null)
            {
                foreach (var param in operation.OutputVariables)
                { 
                    if (!psOutVariables.ContainsKey(param))
                        psOutVariables.Add(param, null);
                } 
            }

            if (executionMode == PsExecutionMode.Collect || executionMode == PsExecutionMode.Configure)
            {
                if (scriptInfo?.ConfigParameters?.Count > 0)
                {
                    var uniqueConfigKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var configParam in scriptInfo.ConfigParameters)
                    {
                        var uniqueKey = $"{configParam.ConfigType},{configParam.ConfigKey}";
                        if (!uniqueConfigKeys.Add(uniqueKey))
                        {
                            operation.LogWarning(
                                "There are duplicate configuration type/key (AHCONFIGKEY, AHCONFIGTYPE) values specified in the script. " +
                                $"Only the first set ({uniqueKey}) will be used."
                            );
                            continue;
                        }

                        setOutputVariable(configParam.ConfigType);
                        setOutputVariable(configParam.ConfigKey);
                        setOutputVariable(configParam.DesiredValue);
                        setOutputVariable(configParam.CurrentValue);
                        setOutputVariable(configParam.ValueDrifted);
                    }

                    void setOutputVariable(string oe)
                    {
                        if (oe?.StartsWith('$') == true)
                            psOutVariables.Add(oe.TrimStart('$'), oe.TrimStart('$'));
                    }
                }

                if (!string.IsNullOrWhiteSpace(scriptInfo.ExecutionModeVariableName))
                    psVariables[scriptInfo.ExecutionModeVariableName.TrimStart('$')] = executionMode.ToString();
            }

            if (executionMode == PsExecutionMode.Configure && string.IsNullOrEmpty(scriptInfo?.ExecutionModeVariableName))
            {
                operation.LogError(
                    ".AHEXECMODE additional help was not detected. When using PSEnsure to remediate drift, you must specify the name of " +
                    "a variable that will capture \"Collect\" or \"Configure\" in the .AHEXECMODE help."
                );
                return null;
            }

            var jobRunner = context.Agent.GetService<IRemoteJobExecuter>();

            var job = new ExecutePowerShellJob
            {
                ScriptText = scriptContent,
                DebugLogging = false,
                VerboseLogging = true,
                CollectOutput = collectOutput,
                LogOutput = !collectOutput,
                Variables = psVariables,
                Parameters = psParameters,
                OutVariables = [.. psOutVariables.Keys],
                WorkingDirectory = context.WorkingDirectory,
                PreferWindowsPowerShell = preferWindowsPowerShell,
                TerminateHostProcess = context.GetFlagOrDefault<AutoTerminatePowerShellProcessVariableFunction>(defaultValue: true)
            };

            job.MessageLogged += (s, e) => operation.Log(e.Level, e.Message);
            if (progressUpdateHandler != null)
                job.ProgressUpdate += progressUpdateHandler;

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(job, context.CancellationToken);
            PSUtil.LogExit(operation, result.ExitCode, successExitCode);

            var data = new ExecuteScriptResult
            {
                ExitCode = result.ExitCode,
                Output = result.Output,
                OutVariables = result.OutVariables
            };

            foreach (var var in result.OutVariables)
            {
                if (psOutVariables.TryGetValue(var.Key, out var varName))
                    context.SetVariableValue(new RuntimeVariableName(varName, var.Value.ValueType), var.Value);
                else if (operation.OutputVariables?.Contains(var.Key, StringComparer.OrdinalIgnoreCase) == true)
                    context.SetVariableValue(new RuntimeVariableName(var.Key, var.Value.ValueType), var.Value);
            }

            if (executionMode == PsExecutionMode.Collect || executionMode == PsExecutionMode.Configure)
            {
                var configInfos = new List<ExecuteScriptResultConfigurationInfo>();
                if (scriptInfo?.ConfigParameters?.Count > 0)
                {
                    var uniqueConfigKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var usedOutput = false;
                    foreach (var param in scriptInfo.ConfigParameters)
                    {
                        var configType = getOutputExpressionValue(param.ConfigType, "PSConfig").AsString();
                        var configKey = getOutputExpressionValue(param.ConfigKey, new RuntimeValue(operation.ScriptName)).AsString();
                        var uniqueKey = $"{configType},{configKey}";
                        if (!uniqueConfigKeys.Add(uniqueKey))
                        {
                            operation.LogWarning(
                                "This script returned multiple configuration items, but each item does have a unique type/key (AHCONFIGKEY, AHCONFIGTYPE)." +
                                $"Only the first item with the type/key ({uniqueKey}) will be recorded."
                            );
                            continue;
                        }
                        if (string.IsNullOrEmpty(param.CurrentValue))
                        {
                            if (usedOutput)
                            {
                                operation.LogWarning(
                                    "Another configuration item is already using the script output for the current value; when multiple types are returned, " +
                                    "a AHCURRENTVALUE help should be used."
                                );
                                continue;
                            }
                            usedOutput = true;
                        }
                        configInfos.Add(new ExecuteScriptResultConfigurationInfo
                        {
                            ConfigType = configType,
                            ConfigKey = configKey,
                            DesiredConfigValue = getOutputExpressionValue(param.DesiredValue, new RuntimeValue(true)),
                            CurrentConfigValue = getOutputExpressionValue(param.CurrentValue, result.Output.FirstOrDefault()),
                            DriftDetected = getOutputExpressionValue(param.ValueDrifted, null).AsBoolean()
                        });
                    }

                    RuntimeValue getOutputExpressionValue(string oe, RuntimeValue defaultValue)
                    {
                        if (string.IsNullOrEmpty(oe))
                            return defaultValue;

                        if (oe.StartsWith('$') && result.OutVariables.TryGetValue(oe[1..], out var oval))
                        {
                            result.OutVariables.Remove(oe);
                            return oval;
                        }

                        return new RuntimeValue(oe);
                    }
                }
                else
                {
                    operation.LogDebug("Script did not define any additional help configuration parameters, so the default values will be used.");
                    configInfos.Add(new ExecuteScriptResultConfigurationInfo
                    {
                        ConfigType = "PSConfig",
                        ConfigKey = operation.ScriptName,
                        DesiredConfigValue = new RuntimeValue(true),
                        CurrentConfigValue = result.Output.FirstOrDefault()
                    });
                }

                data.Configuration = configInfos;
            }

            return data;
        }
    }
}
