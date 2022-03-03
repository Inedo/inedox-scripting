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
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages.Shell;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    internal static class SHUtil
    {

        /************************************ Legacy Methods ************************************/
        /*
         * SHEnsure
         */

        public static Task<int?> ExecuteScriptAsync(IOperationExecutionContext context, TextReader scriptReader, string arguments, ILogSink logger, bool verbose, MessageLevel outputLevel = MessageLevel.Information, MessageLevel errorLevel = MessageLevel.Error)
        {
            return ExecuteScriptAsync(context, scriptReader, arguments, logger, verbose, data => LogMessage(outputLevel, data, logger), errorLevel);
        }

        public static async Task<int?> ExecuteScriptAsync(IOperationExecutionContext context, TextReader scriptReader, string arguments, ILogSink logger, bool verbose, Action<string> output, MessageLevel errorLevel = MessageLevel.Error)
        {
            var fileOps = context.Agent.TryGetService<ILinuxFileOperationsExecuter>();
            if (fileOps == null)
            {
                logger.LogError("This operation is only valid when run against an SSH agent.");
                return null;
            }

            var scriptsDirectory = fileOps.CombinePath(fileOps.GetBaseWorkingDirectory(), "scripts");
            await fileOps.CreateDirectoryAsync(scriptsDirectory).ConfigureAwait(false);

            var fileName = fileOps.CombinePath(scriptsDirectory, Guid.NewGuid().ToString("N"));
            try
            {
                if (verbose)
                    logger.LogDebug($"Writing script to temporary file at {fileName}...");

                using (var scriptStream = await fileOps.OpenFileAsync(fileName, FileMode.Create, FileAccess.Write, Octal755).ConfigureAwait(false))
                using (var scriptWriter = new StreamWriter(scriptStream, InedoLib.UTF8Encoding) { NewLine = "\n" })
                {
                    var line = await scriptReader.ReadLineAsync().ConfigureAwait(false);
                    while (line != null)
                    {
                        await scriptWriter.WriteLineAsync(line).ConfigureAwait(false);
                        line = await scriptReader.ReadLineAsync().ConfigureAwait(false);
                    }
                }

                if (verbose)
                {
                    logger.LogDebug("Script written successfully.");
                    logger.LogDebug($"Ensuring that working directory ({context.WorkingDirectory}) exists...");
                }

                await fileOps.CreateDirectoryAsync(context.WorkingDirectory).ConfigureAwait(false);

                if (verbose)
                {
                    logger.LogDebug("Working directory is present.");
                    logger.LogDebug("Script file: " + fileName);
                    logger.LogDebug("Arguments: " + arguments);
                    logger.LogDebug("Executing script...");
                }

                var ps = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>().ConfigureAwait(false);
                int? exitCode;

                using (var process = ps.CreateProcess(new RemoteProcessStartInfo { FileName = fileName, WorkingDirectory = context.WorkingDirectory, Arguments = arguments }))
                {
                    process.OutputDataReceived += (s, e) => output(e.Data);
                    process.ErrorDataReceived += (s, e) => LogMessage(errorLevel, e.Data, logger);
                    process.Start();
                    await process.WaitAsync(context.CancellationToken).ConfigureAwait(false);
                    exitCode = process.ExitCode;
                }

                if (verbose)
                    logger.LogDebug("Script completed.");

                return exitCode;
            }
            finally
            {
                if (verbose)
                    logger.LogDebug($"Deleting temporary script file ({fileName})...");

                try
                {
                    fileOps.DeleteFile(fileName);
                    if (verbose)
                        logger.LogDebug("Temporary file deleted.");
                }
                catch (Exception ex)
                {
                    if (verbose)
                        logger.LogDebug("Unable to delete temporary file: " + ex.Message);
                }
            }
        }

        private static void LogMessage(MessageLevel level, string text, ILogSink logger)
        {
            if (!string.IsNullOrWhiteSpace(text))
                logger.Log(level, text);
        }

        public static TextReader OpenScriptAsset(string name, ILogSink logger, IOperationExecutionContext context)
        {
            var scriptName = name;
            if (!scriptName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
                scriptName += ".sh";

            var item = SDK.GetRaftItem(RaftItemType.Script, scriptName, context);
            if (item == null)
            {
                logger.LogError($"Could not find script {scriptName}.");
                return null;
            }

            return new StringReader(item.Content);
        }

        internal static (string RaftName, string ItemName) SplitScriptName(string scriptName)
        {
            if (string.IsNullOrEmpty(scriptName))
                throw new ArgumentNullException(nameof(scriptName));

            int sep = scriptName.IndexOf("::");
            if (sep == -1)
                return (null, scriptName);

            return (scriptName.Substring(0, sep), scriptName.Substring(sep + 2));
        }

        /************************************ End Legacy Methods ************************************/
        /*
         * SHExec
         * SHCall
         * SHVerify2
         * SHEnsure2
         */


        private const int Octal755 = 493;
        private const string OutVarPrefix = "!|OtterScriptOutVars|!";
        
        public static async Task<ExecuteScriptResult> ExecuteShellScriptAsync(this IShellOperation operation, ShellStartInfo startInfo, IOperationExecutionContext context)
        {
            var outVars = startInfo.OutVariables?.ToArray();

            var fileOps = context.Agent.TryGetService<ILinuxFileOperationsExecuter>();
            if (fileOps == null)
            {
                operation.LogError("This operation is only valid when run against an SSH agent.");
                return null;
            }

            var scriptsDirectory = fileOps.CombinePath(fileOps.GetBaseWorkingDirectory(), "scripts");
            await fileOps.CreateDirectoryAsync(scriptsDirectory).ConfigureAwait(false);
            var fileName = fileOps.CombinePath(scriptsDirectory, Guid.NewGuid().ToString("N"));

            try
            {
                Dictionary<string, RuntimeValue> outValues = new Dictionary<string, RuntimeValue>();
                if (operation.Verbose)
                    operation.LogDebug($"Writing script to temporary file at {fileName}...");

                using (var scriptStream = await fileOps.OpenFileAsync(fileName, FileMode.Create, FileAccess.Write, Octal755).ConfigureAwait(false))
                using (var scriptReader = new StringReader(startInfo.ScriptText))
                using (var scriptWriter = new StreamWriter(scriptStream, InedoLib.UTF8Encoding) { NewLine = "\n" })
                {
                    if (startInfo.InjectedVariables != null)
                    {
                        foreach (var var in startInfo.InjectedVariables)
                        {
                            await scriptWriter.WriteLineAsync($"{var.Key}=\"{var.Value}\"");
                        }
                    }

                    if (outVars?.Length > 0)
                    {
                        await scriptWriter.WriteLineAsync("AhScriptWrapper() {");

                        var line = await scriptReader.ReadLineAsync().ConfigureAwait(false);
                        while (line != null)
                        {
                            await scriptWriter.WriteLineAsync(line).ConfigureAwait(false);
                            line = await scriptReader.ReadLineAsync().ConfigureAwait(false);
                        }
                        await scriptWriter.WriteLineAsync("}");
                        await scriptWriter.WriteLineAsync("AhScriptWrapper");
                        foreach (var outVar in outVars)
                        {
                            await scriptWriter.WriteLineAsync($"echo \"{OutVarPrefix}{outVar}:${outVar}\"");
                        }
                    }
                    else
                    {
                        var line = await scriptReader.ReadLineAsync().ConfigureAwait(false);
                        while (line != null)
                        {
                            await scriptWriter.WriteLineAsync(line).ConfigureAwait(false);
                            line = await scriptReader.ReadLineAsync().ConfigureAwait(false);
                        }
                    }
                }

                if (operation.Verbose)
                {
                    operation.LogDebug("Script written successfully.");
                    operation.LogDebug($"Ensuring that working directory ({context.WorkingDirectory}) exists...");
                }

                await fileOps.CreateDirectoryAsync(context.WorkingDirectory).ConfigureAwait(false);

                if (operation.Verbose)
                {
                    operation.LogDebug("Working directory is present.");
                    operation.LogDebug("Script file: " + fileName);
                    operation.LogDebug("Arguments: " + startInfo.CommandLineArguments);
                    operation.LogDebug("Executing script...");
                }

                var execProcess = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
                
                using var process = execProcess.CreateProcess(
                    new RemoteProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = $"{startInfo.CommandLineArguments}",
                        WorkingDirectory = context.WorkingDirectory,
                        UseUTF8ForStandardOutput = true,
                        UseUTF8ForStandardError = true
                    }
                );

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data.StartsWith(OutVarPrefix))
                    {
                        var outVar = e.Data.Substring(OutVarPrefix.Length);
                        var key = outVar.Substring(0, outVar.IndexOf(":"));
                        var value = outVar.Substring(outVar.IndexOf(":")+1);
                        outValues.Add(key, value);
                    }
                    else
                        operation.Log(operation.OutputLevel, e.Data);
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    operation.Log(operation.ErrorLevel, e.Data);
                };

                await process.StartAsync(context.CancellationToken);

                await process.WaitAsync(context.CancellationToken);

                if (operation.Verbose)
                    operation.LogDebug("Script completed.");

                return new ExecuteScriptResult
                {
                    ExitCode = process.ExitCode.GetValueOrDefault(),
                    OutVariables = outValues
                };
            }
            finally
            {
                if (operation.Verbose)
                    operation.LogDebug($"Deleting temporary script file ({fileName})...");

                try
                {
                    fileOps.DeleteFile(fileName);
                    if (operation.Verbose)
                        operation.LogDebug("Temporary file deleted.");
                }
                catch (Exception ex)
                {
                    if (operation.Verbose)
                        operation.LogDebug("Unable to delete temporary file: " + ex.Message);
                }
            }
        }

        public static async Task<ExecuteScriptResult> ExecuteShellScriptAhAsync<TOperation>(this TOperation operation, IOperationExecutionContext context, string execMode)
           where TOperation : IShellOperation, IScriptingOperation
        {
            var scriptText = operation.GetScriptAsset(operation.ScriptName, context);
            if (scriptText == null)
            {
                operation.LogError($"Script {operation.ScriptName} was not found.");
                return null;
            }

            var inputVars = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var argVars = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var ahOutVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var scriptInfo = ScriptParser.Parse<ShellScriptParser>(scriptText);

            if (operation.Parameters != null)
            {
                foreach (var paramValue in operation.Parameters)
                {
                    var paramInfo = scriptInfo.Parameters.FirstOrDefault(p => string.Equals(p.Name, paramValue.Key, StringComparison.OrdinalIgnoreCase));
                    if (paramInfo == null || paramInfo.Usage == ScriptParameterUsage.InputVariable || paramInfo.Usage == ScriptParameterUsage.Default)
                        inputVars[paramValue.Key] = paramValue.Value;
                    else if (paramInfo.Usage == ScriptParameterUsage.Arguments)
                        argVars[paramValue.Key] = paramValue.Value;
                    else if (paramInfo.Usage == ScriptParameterUsage.OutputVariable)
                        ahOutVars.Add(paramValue.Key);
                    else
                        throw new InvalidOperationException($"Parameter \"{paramValue.Key}\" contains an unsupported usage type: {paramInfo.Usage}.");
                }
            }

            foreach (var p in scriptInfo.Parameters.Where(p => p.Usage == ScriptParameterUsage.Arguments && !string.IsNullOrEmpty(p.DefaultValue)))
            {
                if (!argVars.ContainsKey(p.Name))
                    argVars[p.Name] = p.DefaultValue;
            }

            var commandLineArgs = string.Empty;

            if (argVars.Count > 0 && string.IsNullOrWhiteSpace(scriptInfo.DefaultArgumentsFormat))
                operation.LogWarning("Command line arguments have been specified in AhParameters, but no AhArgumentsFormat string was specified.");
            else if (!string.IsNullOrWhiteSpace(scriptInfo.DefaultArgumentsFormat))
                commandLineArgs = (await context.ExpandVariablesAsync(scriptInfo.DefaultArgumentsFormat, argVars)).AsString();

            if (operation.InputVariables != null)
            {
                foreach (var v in operation.InputVariables)
                    inputVars[v.Key] = v.Value;
            }

            if (!string.IsNullOrWhiteSpace(scriptInfo.ExecModeVariable))
                inputVars.Add(scriptInfo.ExecModeVariable.TrimStart('$'), execMode);

            var originalOutVars = operation.OutputVariables?.ToList() ?? new List<string>();

            if (operation.OutputVariables != null)
                ahOutVars.UnionWith(operation.OutputVariables);

            foreach (var c in scriptInfo.ConfigValues)
            {
                addOutVar(c.ConfigKey);
                addOutVar(c.ConfigType);
                addOutVar(c.CurrentValue);
                addOutVar(c.DesiredValue);
                addOutVar(c.ValueDrifted);
            }

            var ahStartInfo = new ShellStartInfo
            {
                ScriptText = scriptText,
                InjectedVariables = inputVars,
                OutVariables = ahOutVars.ToList(),
                CommandLineArguments = commandLineArgs
            };

            var result = await operation.ExecuteShellScriptAsync(ahStartInfo, context);

            result.Configuration = new List<ExecuteScriptResultConfigurationInfo>();

            if (result.OutVariables != null)
            {
                foreach (var c in scriptInfo.ConfigValues)
                {
                    result.Configuration.Add(
                        new ExecuteScriptResultConfigurationInfo
                        {
                            ConfigKey = tryGetOutVar(c.ConfigKey),
                            ConfigType = tryGetOutVar(c.ConfigType),
                            CurrentConfigValue = tryGetOutVar(c.CurrentValue),
                            DesiredConfigValue = tryGetOutVar(c.DesiredValue),
                            DriftDetected = bool.TryParse(tryGetOutVar(c.ValueDrifted), out bool b) ? b : null
                        }
                    );
                }
               
                foreach (var outParam in scriptInfo?.Parameters?.Where(p => p.Usage == ScriptParameterUsage.OutputVariable && ahOutVars.Contains(p.Name)).ToList() ?? new List<ScriptParameterInfo>())
                {
                    var param = operation.Parameters[outParam.Name].AsString();
                    result.OutVariables[param] = result.OutVariables[outParam.Name];

                    if(!originalOutVars.Contains(param))
                        originalOutVars.Add(param);
                }

                foreach (var v in ahOutVars.Except(originalOutVars, StringComparer.OrdinalIgnoreCase))
                    result.OutVariables.Remove(v);
            }

            return result;

            void addOutVar(string value)
            {
                if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("$"))
                    ahOutVars.Add(value.Substring(1));
            }

            string tryGetOutVar(string varName)
            {
                if (varName?.StartsWith("$") == true)
                {
                    if (result.OutVariables.TryGetValue(varName.Substring(1), out var value))
                        return value.AsString();
                    else
                        return null;
                }
                else
                {
                    return AH.NullIf(varName, string.Empty);
                }
            }
        }

        public static string GetScriptAsset(this IShellOperation operation, string name, IOperationExecutionContext context)
        {
            var scriptName = name;
            if (!scriptName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
                scriptName += ".sh";

            var item = SDK.GetRaftItem(RaftItemType.Script, scriptName, context);
            if (item == null)
            {
                operation.LogError($"Could not find script {scriptName}.");
                return null;
            }

            return item.Content;
        }
    }
}
