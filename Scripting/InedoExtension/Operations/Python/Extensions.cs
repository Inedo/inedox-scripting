using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages.Python;
using Inedo.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inedo.Extensions.Scripting.Operations.Python
{
    internal static class Extensions
    {
        private const string OutVarPrefix = "!|OtterScriptOutVars|!";
        private static readonly LazyRegex LogMessageRegex = new(@"^!\|AH:(?<1>[A-Z]+)\|!(?<2>.*)$");

        public static async Task<ExecuteScriptResult> ExecutePythonScriptAsync(this IPythonOperation operation, PythonStartInfo startInfo, IOperationExecutionContext context)
        {
            var pythonPath = await operation.GetPythonExePathAsync(context);
            var outVars = startInfo.OutVariables?.ToArray();

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var scriptsDirectory = fileOps.CombinePath(fileOps.GetBaseWorkingDirectory(), "scripts");
            await fileOps.CreateDirectoryAsync(scriptsDirectory).ConfigureAwait(false);
            var fileName = fileOps.CombinePath(scriptsDirectory, $"{Guid.NewGuid():N}.py");
            try
            {
                Dictionary<string, RuntimeValue> outValues = null;

                using (var scriptWriter = new StreamWriter(await fileOps.OpenFileAsync(fileName, FileMode.Create, FileAccess.Write)))
                {
                    scriptWriter.WriteLine("import base64");
                    scriptWriter.WriteLine("import json");
                    scriptWriter.WriteLine("import logging");
                    scriptWriter.WriteLine("import sys");
                    scriptWriter.WriteLine("import traceback");
                    scriptWriter.WriteLine($"logging.basicConfig(format='!|AH:%(levelname)s|!%(message)s', level=logging.{(operation.CaptureDebug ? "DEBUG" : "INFO")})");

                    if (startInfo.InjectedVariables != null)
                    {
                        foreach (var var in startInfo.InjectedVariables)
                        {
                            scriptWriter.Write(var.Key);
                            scriptWriter.Write(" = json.loads(");
                            WriteStringifiedJson(scriptWriter, var.Value);
                            scriptWriter.WriteLine(')');
                        }
                    }

                    scriptWriter.WriteLine("try:");
                    scriptWriter.Write("\texec(base64.b64decode(b'");
                    scriptWriter.Write(Convert.ToBase64String(InedoLib.UTF8Encoding.GetBytes(startInfo.ScriptText)));
                    scriptWriter.WriteLine("').decode('utf-8'))");

                    scriptWriter.WriteLine("except SyntaxError as err:");
                    scriptWriter.WriteLine("\tprint(f'{err.__class__.__name__} at line {err.lineno} of Python script: {err.args[0]}', file=sys.stderr)");

                    scriptWriter.WriteLine("except Exception as err:");
                    scriptWriter.WriteLine("\tcl, exc, tb = sys.exc_info()");
                    scriptWriter.WriteLine("\tprint(f'{err.__class__.__name__} at line {traceback.extract_tb(tb)[-1][1]} of Python script: {err.args[0]}', file=sys.stderr)");

                    if (outVars?.Length > 0)
                    {
                        scriptWriter.WriteLine("else:");
                        scriptWriter.Write($"\tprint('{OutVarPrefix}' + json.dumps({{");
                        scriptWriter.Write(string.Join(", ", outVars.Select(v => $"\"{v}\": {v}")));
                        scriptWriter.WriteLine("}))");
                    }
                }

                await fileOps.CreateDirectoryAsync(context.WorkingDirectory);

                var execProcess = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
                using var process = execProcess.CreateProcess(
                    new RemoteProcessStartInfo
                    {
                        FileName = pythonPath,
                        Arguments = $"-X utf8 \"{fileName}\" {startInfo.CommandLineArguments}",
                        WorkingDirectory = context.WorkingDirectory,
                        UseUTF8ForStandardOutput = true,
                        UseUTF8ForStandardError = true
                    }.WithEnvironmentVariables(startInfo.EnvironmentVariables)
                );

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data.StartsWith(OutVarPrefix))
                        outValues = ReadJsonObject(JObject.Parse(e.Data.Substring(OutVarPrefix.Length)));
                    else
                        operation.LogInformation(e.Data);
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    var m = LogMessageRegex.Match(e.Data);
                    if (m.Success)
                    {
                        var level = m.Groups[1].Value switch
                        {
                            "DEBUG" => MessageLevel.Debug,
                            "INFO" => MessageLevel.Information,
                            "WARNING" => MessageLevel.Warning,
                            "ERROR" or "CRITICAL" => MessageLevel.Error,
                            _ => MessageLevel.Debug
                        };

                        operation.Log(level, m.Groups[2].Value);
                    }
                    else
                    {
                        operation.LogError(e.Data);
                    }
                };

                await process.StartAsync(context.CancellationToken);

                await process.WaitAsync(context.CancellationToken);

                return new ExecuteScriptResult
                {
                    ExitCode = process.ExitCode.GetValueOrDefault(),
                    OutVariables = outValues
                };
            }
            finally
            {
                try
                {
                    await fileOps.DeleteFileAsync(fileName);
                }
                catch
                {
                }
            }
        }

        public static async Task<ExecuteScriptResult> ExecutePythonScriptAhAsync<TOperation>(this TOperation operation, IOperationExecutionContext context, string execMode)
            where TOperation : IPythonOperation, IScriptingOperation
        {
            var scriptText = operation.GetScriptAsset(operation.ScriptName, context);
            if (scriptText == null)
            {
                operation.LogError($"Script {operation.ScriptName} was not found.");
                return null;
            }

            var inputVars = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var argVars = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var ahOutVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var scriptInfo = ScriptParser.Parse<PythonScriptParser>(scriptText);

            if (operation.Parameters != null)
            {
                foreach (var paramValue in operation.Parameters)
                {
                    var paramInfo = scriptInfo.Parameters.FirstOrDefault(p => string.Equals(p.Name, paramValue.Key, StringComparison.OrdinalIgnoreCase));
                    if (paramInfo == null || paramInfo.Usage == ScriptParameterUsage.InputVariable || paramInfo.Usage == ScriptParameterUsage.Default)
                        inputVars[paramValue.Key] = paramValue.Value;
                    else if (paramInfo.Usage == ScriptParameterUsage.EnvironmentVariable)
                        envVars[paramValue.Key] = paramValue.Value.AsString();
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

            var ahStartInfo = new PythonStartInfo
            {
                ScriptText = scriptText,
                InjectedVariables = inputVars,
                OutVariables = ahOutVars.ToList(),
                EnvironmentVariables = envVars,
                CommandLineArguments = commandLineArgs
            };

            var result = await operation.ExecutePythonScriptAsync(ahStartInfo, context);

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

                    if (!originalOutVars.Contains(param))
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

        public static string GetScriptAsset(this IPythonOperation operation, string name, IOperationExecutionContext context)
        {
            var scriptName = name;
            if (!scriptName.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                scriptName += ".py";

            var item = SDK.GetRaftItem(RaftItemType.Script, scriptName, context);
            if (item == null)
            {
                operation.LogError($"Could not find script {scriptName}.");
                return null;
            }

            return item.Content;
        }

        private static async Task<string> GetPythonExePathAsync(this IPythonOperation operation, IOperationExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(operation.PythonPath))
            {
                operation.LogDebug("PythonPath is not defined; searching for python...");

                string foundPath = null;

                var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
                if (fileOps.DirectorySeparator == '/')
                {
                    if (await fileOps.FileExistsAsync("/bin/python3"))
                        foundPath = "/bin/python3";
                }
                else
                {
                    var rubbish = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
                    var programFilesDir = await rubbish.GetEnvironmentVariableValueAsync("ProgramFiles");

                    var path = await getBestVersionAsync(programFilesDir);
                    if (path == null)
                    {
                        var userPythonDir = fileOps.CombinePath(await rubbish.GetEnvironmentVariableValueAsync("LocalAppData"), "Programs", "Python");
                        if (await fileOps.DirectoryExistsAsync(userPythonDir))
                            path = await getBestVersionAsync(userPythonDir);
                    }

                    foundPath = path;

                    async Task<string> getBestVersionAsync(string searchPath)
                    {
                        var dirs = from d in await fileOps.GetFileSystemInfosAsync(searchPath, new MaskingContext(new[] { "Python3*" }, Enumerable.Empty<string>()))
                                   where d is SlimDirectoryInfo && d.Name.StartsWith("Python3")
                                   let ver = AH.ParseInt(d.Name.Substring("Python3".Length))
                                   where ver.HasValue
                                   orderby ver descending
                                   select d.FullName;

                        foreach (var dir in dirs)
                        {
                            var path = fileOps.CombinePath(dir, "python.exe");
                            if (await fileOps.FileExistsAsync(path))
                                return path;
                        }

                        return null;
                    }
                }

                if (foundPath == null)
                    throw new ExecutionFailureException("Could not find python interpreter and $PythonPath configuration variable is not set.");

                operation.LogDebug("Using python at: " + foundPath);
                return foundPath;
            }
            else
            {
                return context.ResolvePath(operation.PythonPath);
            }
        }
        private static void WriteStringifiedJson(TextWriter writer, RuntimeValue value)
        {
            writer.Write('\'');

            using var stringWriter = new StringWriter();
            using (var jsonWriter = new JsonTextWriter(stringWriter))
            {
                WriteJson(jsonWriter, value);
            }

            foreach (char c in stringWriter.ToString())
            {
                if (c == '\'' || c == '\\')
                    writer.Write('\\');

                writer.Write(c);
            }

            writer.Write('\'');
        }
        private static void WriteJson(JsonTextWriter writer, RuntimeValue value)
        {
            switch (value.ValueType)
            {
                case RuntimeValueType.Scalar:
                    writer.WriteValue(value.AsString());
                    break;

                case RuntimeValueType.Vector:
                    writer.WriteStartArray();
                    foreach (var item in value.AsEnumerable())
                        WriteJson(writer, item);
                    writer.WriteEndArray();
                    break;

                case RuntimeValueType.Map:
                    writer.WriteStartObject();
                    foreach (var prop in value.AsDictionary())
                    {
                        writer.WritePropertyName(prop.Key);
                        WriteJson(writer, prop.Value);
                    }

                    writer.WriteEndObject();
                    break;

                default:
                    throw new NotSupportedException($"{value.ValueType} variables are not supported.");
            }
        }
        private static Dictionary<string, RuntimeValue> ReadJsonObject(JObject obj)
        {
            var dict = new Dictionary<string, RuntimeValue>();
            foreach (var prop in obj.Properties())
                dict[prop.Name] = ReadJsonToken(prop.Value);

            return dict;
        }
        private static RuntimeValue ReadJsonToken(JToken token)
        {
            return token switch
            {
                JObject obj => new RuntimeValue(ReadJsonObject(obj)),
                JArray arr => new RuntimeValue(arr.Select(ReadJsonToken).ToList()),
                JValue v => new RuntimeValue(v.Value?.ToString()),
                _ => default
            };
        }
    }
}
