using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.ExecutionEngine;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;

namespace Inedo.Extensions.Scripting.PowerShell
{
    internal static class PSUtil
    {
        public static Task<ExecuteScriptResult> ExecuteScriptAsync(ILogSink logger, IOperationExecutionContext context, string scriptNameOrContent, bool scriptIsAsset, IReadOnlyDictionary<string, RuntimeValue> arguments, IDictionary<string, RuntimeValue> outArguments, bool collectOutput, EventHandler<PSProgressEventArgs> progressUpdateHandler, string successExitCode = null)
        {
            if (scriptIsAsset)
                return ExecuteScriptAssetAsync(logger, context, scriptNameOrContent, arguments, outArguments, collectOutput, progressUpdateHandler, successExitCode);
            else
                return ExecuteScriptDirectAsync(logger, context, scriptNameOrContent, arguments, outArguments, collectOutput, progressUpdateHandler, successExitCode);
        }
        public static Task<ExecuteScriptResult> ExecuteScriptAssetAsync(ILogSink logger, IOperationExecutionContext context, string fullScriptName, IReadOnlyDictionary<string, RuntimeValue> arguments, IDictionary<string, RuntimeValue> outArguments, bool collectOutput, EventHandler<PSProgressEventArgs> progressUpdateHandler, string successExitCode = null, PsExecutionMode executionMode = PsExecutionMode.Normal)
        {
            var scriptText = GetScriptText(logger, fullScriptName, context);
            if (scriptText == null)
                return Task.FromResult<ExecuteScriptResult>(null);

            return ExecuteScriptDirectAsync(logger, context, scriptText, arguments, outArguments, collectOutput, progressUpdateHandler, successExitCode, executionMode, fullScriptName);
        }
        public static async Task<ExecuteScriptResult> ExecuteScriptDirectAsync(ILogSink logger, IOperationExecutionContext context, string scriptText, IReadOnlyDictionary<string, RuntimeValue> arguments, IDictionary<string, RuntimeValue> outArguments, bool collectOutput, EventHandler<PSProgressEventArgs> progressUpdateHandler, string successExitCode = null, PsExecutionMode executionMode = PsExecutionMode.Normal, string fullScriptName = null, bool preferWindowsPowerShell = true)
        {
            var variables = new Dictionary<string, RuntimeValue>();
            var parameters = new Dictionary<string, RuntimeValue>();

            if (PowerShellScriptInfo.TryParse(new StringReader(scriptText), out var scriptInfo))
            {
                foreach (var var in arguments)
                {
                    var value = var.Value;
                    var param = scriptInfo.Parameters.FirstOrDefault(p => string.Equals(p.Name, var.Key, StringComparison.OrdinalIgnoreCase));
                    if (param != null && param.IsBooleanOrSwitch)
                        value = value.AsBoolean() ?? false;
                    if (param != null)
                        parameters[param.Name] = value;
                    else
                        variables[var.Key] = value;
                }

                if (executionMode == PsExecutionMode.Collect || executionMode == PsExecutionMode.Configure)
                {
                    if (scriptInfo.ConfigParameters?.Count > 0)
                    {
                        var uniqueConfigKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        void setOutputVariable(string oe)
                        {
                            if (oe?.StartsWith("$") == true)
                                outArguments[oe.TrimStart('$')] = string.Empty;
                        }
                        foreach (var configParam in scriptInfo.ConfigParameters)
                        {
                            var uniqueKey = $"{configParam.ConfigType},{configParam.ConfigKey}";
                            if (!uniqueConfigKeys.Add(uniqueKey))
                            {
                                logger.LogWarning(
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
                    }

                    if (!string.IsNullOrWhiteSpace(scriptInfo.ExecutionModeVariableName))
                        variables[scriptInfo.ExecutionModeVariableName.TrimStart('$')] = executionMode.ToString();
                }
            }
            else
            {
                variables = arguments.ToDictionary(a => a.Key, a => a.Value);
            }

            if (executionMode == PsExecutionMode.Configure && string.IsNullOrEmpty(scriptInfo?.ExecutionModeVariableName))
            {
                logger.LogError(
                    ".AHEXECMODE additional help was not detected. When using PSEnsure to remediate drift, you must specify the name of " +
                    "a variable that will capture \"Collect\" or \"Configure\" in the .AHEXECMODE help."
                );
                return null;
            }

            var jobRunner = context.Agent.GetService<IRemoteJobExecuter>();

            var job = new ExecutePowerShellJob
            {
                ScriptText = scriptText,
                DebugLogging = false,
                VerboseLogging = true,
                CollectOutput = collectOutput,
                LogOutput = !collectOutput,
                Variables = variables,
                Parameters = parameters,
                OutVariables = outArguments.Keys.ToArray(),
                WorkingDirectory = context.WorkingDirectory,
                PreferWindowsPowerShell = preferWindowsPowerShell
            };

            job.MessageLogged += (s, e) => logger.Log(e.Level, e.Message);
            if (progressUpdateHandler != null)
                job.ProgressUpdate += progressUpdateHandler;

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(job, context.CancellationToken);
            LogExit(logger, result.ExitCode, successExitCode);

            foreach (var var in result.OutVariables)
                outArguments[var.Key] = var.Value;

            var data = new ExecuteScriptResult
            {
                ExitCode = result.ExitCode,
                Output = result.Output,
                OutVariables = result.OutVariables
            };

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
                        var configKey = getOutputExpressionValue(param.ConfigKey, new RuntimeValue(fullScriptName)).AsString();
                        var uniqueKey = $"{configType},{configKey}";
                        if (!uniqueConfigKeys.Add(uniqueKey))
                        {
                            logger.LogWarning(
                                "This script returned multiple configuration items, but each item does have a unique type/key (AHCONFIGKEY, AHCONFIGTYPE)." +
                                $"Only the first item with the type/key ({uniqueKey}) will be recorded."
                            );
                            continue;
                        }
                        if (string.IsNullOrEmpty(param.CurrentValue))
                        {
                            if (usedOutput)
                            {
                                logger.LogWarning(
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

                        if (oe.StartsWith("$") && result.OutVariables.TryGetValue(oe.Substring(1), out var oval))
                        {
                            result.OutVariables.Remove(oe);
                            return oval;
                        }
                        return new RuntimeValue(oe);
                    }
                }
                else
                {
                    logger.LogDebug("Script did not define any additional help configuration parameters, so the default values will be used.");
                    configInfos.Add(new ExecuteScriptResultConfigurationInfo
                    {
                        ConfigType = "PSConfig",
                        ConfigKey = fullScriptName,
                        DesiredConfigValue = new RuntimeValue(true),
                        CurrentConfigValue = result.Output.FirstOrDefault()
                    });
                }
                data.Configuration = configInfos;
            }
            
            return data;
        }

        public static RuntimeValue ToRuntimeValue(object value)
        {
            if (value is PSObject psObject)
            {
                if (psObject.BaseObject is IDictionary dictionary)
                    return new RuntimeValue(dictionary.Keys.Cast<object>().ToDictionary(k => k?.ToString(), k => ToRuntimeValue(dictionary[k])));

                if (psObject.BaseObject is IConvertible)
                    return new RuntimeValue(psObject.BaseObject.ToString());

                var d = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in psObject.Properties)
                {
                    if (p.IsGettable && p.IsInstance)
                        d[p.Name] = ToRuntimeValue(p.Value);
                }

                return new RuntimeValue(d);
            }

            if (value is IDictionary dict)
                return new RuntimeValue(dict.Keys.Cast<object>().ToDictionary(k => k?.ToString(), k => ToRuntimeValue(dict[k])));

            if (value is IConvertible)
                return new RuntimeValue(value?.ToString());

            if (value is IEnumerable e)
            {
                var list = new List<RuntimeValue>();
                foreach (var item in e)
                    list.Add(ToRuntimeValue(item));

                return new RuntimeValue(list);
            }

            return new RuntimeValue(value?.ToString());
        }

        private static string GetScriptText(ILogSink logger, string fullScriptName, IOperationExecutionContext context)
        {
            var scriptName = fullScriptName;
            if (!scriptName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                scriptName += ".ps1";

            var scriptItem = SDK.GetRaftItem(RaftItemType.Script, scriptName, context);

            if (scriptItem == null)
            {
                logger.LogError($"Script {scriptName} not found.");
                return null;
            }

            return scriptItem.Content;
        }

        public static void LogExit(ILogSink logger, int? exitCode, string successExitCode = null)
        {
            if (!exitCode.HasValue)
                return;

            var comparator = ExitCodeComparator.TryParse(successExitCode);
            if (comparator != null && !comparator.Evaluate(exitCode.Value))
            {
                logger.LogError("Script exit code: " + exitCode);
            }
            else
            {
                logger.LogDebug("Script exit code: " + exitCode);
            }
        }

        public static IEnumerable<RuntimeValue> ParseDictionary(this RuntimeValue val)
        {
            if ((val.AsDictionary()?.Keys.Count ?? 0) <= 0)
                return null;
            return new List<RuntimeValue> { val };
        }

        private sealed class ExitCodeComparator
        {
            private static readonly string[] ValidOperators = new[] { "=", "==", "!=", "<", ">", "<=", ">=" };

            private ExitCodeComparator(string op, int value)
            {
                this.Operator = op;
                this.Value = value;
            }

            public string Operator { get; }
            public int Value { get; }

            public static ExitCodeComparator TryParse(string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return null;

                var match = Regex.Match(s, @"^\s*(?<1>[=<>!])*\s*(?<2>[0-9]+)\s*$", RegexOptions.ExplicitCapture);
                if (!match.Success)
                    return null;

                var op = match.Groups[1].Value;
                if (string.IsNullOrEmpty(op) || !ValidOperators.Contains(op))
                    op = "==";

                return new ExitCodeComparator(op, int.Parse(match.Groups[2].Value));
            }

            public bool Evaluate(int exitCode)
            {
                return this.Operator switch
                {
                    "=" or "==" => exitCode == this.Value,
                    "!=" => exitCode != this.Value,
                    "<" => exitCode < this.Value,
                    ">" => exitCode > this.Value,
                    "<=" => exitCode <= this.Value,
                    ">=" => exitCode >= this.Value,
                    _ => false
                };
            }
        }
    }
}
