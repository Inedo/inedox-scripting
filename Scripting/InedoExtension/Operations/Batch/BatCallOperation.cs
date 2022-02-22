using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages.WindowsBatch;
using Inedo.IO;
namespace Inedo.Extensions.Scripting.Operations.Batch
{
    [ScriptAlias("BATCall")]
    [DisplayName("BATCall")]
    [DefaultProperty(nameof(ScriptName))]
    [Description("Executes an OtterScript Script stored in a raft.")]
    public sealed class BatCallOperation : ExecuteOperation, IScriptingOperation
    {
        [Required]
        [ScriptAlias("ScriptName")]
        [DisplayName("Script name")]
        public string ScriptName { get; set; }

        [ScriptAlias("Arguments")]
        [DisplayName("Arguments")]
        [Description("Specify the commandline arguments to pass to the batch file.")]
        public string Arguments { get; set; }

        [ScriptAlias("EnvironmentVariables")]
        [DisplayName("Environment variables")]
        [Description("Specify variables to set before running the batch script.")]
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; }

        [ScriptAlias("Parameters")]
        public IReadOnlyDictionary<string, RuntimeValue> Parameters { get; set; }

        IReadOnlyDictionary<string, RuntimeValue> IScriptingOperation.InputVariables { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        IEnumerable<string> IScriptingOperation.OutputVariables { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }


        ScriptLanguageInfo IScriptingOperation.ScriptLanguage => new WindowsBatchScriptLanguage();

        private static readonly LazyRegex LogMessageRegex = new(@"(?<1>[A-Z]+):(?<2>.*)$");

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();

            var scriptItem = SDK.GetRaftItem(RaftItemType.Script, this.ScriptName, context);
            if (scriptItem == null)
            {
                this.LogError($"{scriptItem} was not found.");
                return;
            }
            var scriptInfo = ScriptParser.Parse<WindowsBatchScriptParser>(scriptItem.Content);

            var inputVars = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var argVars = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            if (this.Parameters != null)
            {
                foreach (var paramValue in this.Parameters)
                {
                    var paramInfo = scriptInfo.Parameters.FirstOrDefault(p => string.Equals(p.Name, paramValue.Key, StringComparison.OrdinalIgnoreCase));
                    if (paramInfo == null || paramInfo.Usage == ScriptParameterUsage.Arguments || paramInfo.Usage == ScriptParameterUsage.Default)
                        argVars[paramValue.Key] = paramValue.Value;
                    else if (paramInfo.Usage == ScriptParameterUsage.EnvironmentVariable)
                        envVars[paramValue.Key] = paramValue.Value.AsString();
                    else if (paramInfo.Usage == ScriptParameterUsage.InputVariable)
                        inputVars[paramValue.Key] = paramValue.Value; 
                }
            }

            foreach (var p in scriptInfo.Parameters.Where(p => p.Usage == ScriptParameterUsage.Arguments && !string.IsNullOrEmpty(p.DefaultValue)))
            {
                if (!argVars.ContainsKey(p.Name))
                    argVars[p.Name] = p.DefaultValue;
            }

            var commandLineArgs = string.Empty;

            // default to space 
            if (argVars.Count > 0 && string.IsNullOrWhiteSpace(scriptInfo.DefaultArgumentsFormat))
                this.LogWarning("Command line arguments have been specified in AhParameters, but no AhArgumentsFormat string was specified.");
            else if (!string.IsNullOrWhiteSpace(scriptInfo.DefaultArgumentsFormat))
                commandLineArgs = (await context.ExpandVariablesAsync(scriptInfo.DefaultArgumentsFormat, argVars)).AsString();


            if(!string.IsNullOrEmpty(this.Arguments))
            {
                commandLineArgs += string.IsNullOrEmpty(commandLineArgs) ? "" : $" {this.Arguments}";
            }


            // this doesn't seem to actually work for some reason
            var fileName = fileOps.CombinePath(context.WorkingDirectory, PathEx.GetFileName(this.ScriptName));
            try
            {
                var proc = new RemoteProcessStartInfo
                {
                    Arguments = "/c " + fileName + (string.IsNullOrEmpty(commandLineArgs) ? "" : $" {commandLineArgs}"),
                    FileName = "cmd.exe",
                    WorkingDirectory = context.WorkingDirectory,
                };
                if (this.EnvironmentVariables != null)
                    proc.EnvironmentVariables.AddRange(this.EnvironmentVariables);

                this.LogDebug($"Creating file \"{fileName}\"...");
                await fileOps.CreateDirectoryAsync(PathEx.GetDirectoryName(fileName));
                using (var scriptWriter = new StreamWriter(await fileOps.OpenFileAsync(fileName, FileMode.CreateNew, FileAccess.ReadWrite)))
                {
                    scriptWriter.Write(scriptItem.Content);
                }
                
                await this.ExecuteCommandLineAsync(context, proc);
            }
            finally
            {
                this.LogDebug($"Deleting file \"{fileName}\"...");
                await fileOps.DeleteFileAsync(fileName);
            }

        }

        protected override void LogProcessError(string text)
        {
            var m = LogMessageRegex.Match(text);
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

                this.Log(level, m.Groups[2].Value);
            }
            else
            {
                this.LogDebug(text);
            }
        }

        protected override void LogProcessOutput(string text)
        {
            var m = LogMessageRegex.Match(text);
            if (m.Success)
            {
                var level = m.Groups[1].Value switch
                {
                    "DEBUG" => MessageLevel.Debug,
                    "INFO" or "SUCCESS" => MessageLevel.Information,
                    "WARNING" => MessageLevel.Warning,
                    "ERROR" or "CRITICAL" => MessageLevel.Error,
                    _ => MessageLevel.Debug
                };

                this.Log(level, m.Groups[2].Value);
            }
            else
            {
                this.LogDebug(text);
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "BATCall", new Hilite(config[nameof(ScriptName)])
                ),
                new RichDescription(
                    AH.CoalesceString(config[nameof(Arguments)], "with no arguments")
                )
            );
        }
    }

    public static class EnumerableExtensions
    {
        public static void AddRange<T>(this ICollection<T> source, IEnumerable<T> range)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (range == null)
                throw new ArgumentNullException(nameof(range));
            foreach (var item in range)
                source.Add(item);
        }
    }
}