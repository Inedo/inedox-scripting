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
        public static Task<ExecuteScriptResult> ExecuteScriptAsync(ILogSink logger, IOperationExecutionContext context, string scriptNameOrContent, bool scriptIsAsset, IReadOnlyDictionary<string, RuntimeValue> arguments, IDictionary<string, RuntimeValue> outArguments, bool collectOutput, EventHandler<PSProgressEventArgs> progressUpdateHandler, string successExitCode = null, bool useAhDirectives = false, string executionMode = "Configure")
        {
            if (scriptIsAsset)
                return ExecuteScriptAssetAsync(logger, context, scriptNameOrContent, arguments, outArguments, collectOutput, progressUpdateHandler, successExitCode, useAhDirectives, executionMode);
            else
                return ExecuteScriptDirectAsync(logger, context, scriptNameOrContent, arguments, outArguments, collectOutput, progressUpdateHandler, successExitCode, useAhDirectives, executionMode);
        }
        public static Task<ExecuteScriptResult> ExecuteScriptAssetAsync(ILogSink logger, IOperationExecutionContext context, string fullScriptName, IReadOnlyDictionary<string, RuntimeValue> arguments, IDictionary<string, RuntimeValue> outArguments, bool collectOutput, EventHandler<PSProgressEventArgs> progressUpdateHandler, string successExitCode = null, bool useAhDirectives = false, string executionMode = "Configure")
        {
            var scriptText = GetScriptText(logger, fullScriptName, context);
            if (scriptText == null)
                return Task.FromResult<ExecuteScriptResult>(null);

            return ExecuteScriptDirectAsync(logger, context, scriptText, arguments, outArguments, collectOutput, progressUpdateHandler, successExitCode, useAhDirectives, executionMode);
        }
        public static async Task<ExecuteScriptResult> ExecuteScriptDirectAsync(ILogSink logger, IOperationExecutionContext context, string scriptText, IReadOnlyDictionary<string, RuntimeValue> arguments, IDictionary<string, RuntimeValue> outArguments, bool collectOutput, EventHandler<PSProgressEventArgs> progressUpdateHandler, string successExitCode = null, bool useAhDirectives = false, string executionMode = "Configure")
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

                if (useAhDirectives)
                {
                    if (!string.IsNullOrWhiteSpace(scriptInfo.ConfigKeyVariableName))
                        outArguments[scriptInfo.ConfigKeyVariableName.TrimStart('$')] = string.Empty;

                    if (!string.IsNullOrWhiteSpace(scriptInfo.ConfigValueVariableName))
                        outArguments[scriptInfo.ConfigValueVariableName.TrimStart('$')] = string.Empty;

                    if (!string.IsNullOrWhiteSpace(scriptInfo.ExecutionModeVariableName))
                        variables[scriptInfo.ExecutionModeVariableName.TrimStart('$')] = executionMode;
                }
            }
            else
            {
                variables = arguments.ToDictionary(a => a.Key, a => a.Value);
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
                WorkingDirectory = context.WorkingDirectory
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

            if (useAhDirectives && scriptInfo != null)
            {
                if (!string.IsNullOrWhiteSpace(scriptInfo.ConfigKeyVariableName) && result.OutVariables.TryGetValue(scriptInfo.ConfigKeyVariableName, out var configKey))
                {
                    data.ConfigKey = configKey.AsString();
                    result.OutVariables.Remove(scriptInfo.ConfigKeyVariableName);
                }

                if (!string.IsNullOrWhiteSpace(scriptInfo.ConfigValueVariableName) && result.OutVariables.TryGetValue(scriptInfo.ConfigValueVariableName, out var configValue))
                {
                    data.ConfigValue = configValue;
                    result.OutVariables.Remove(scriptInfo.ConfigValueVariableName);
                }
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
