using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages;
using Inedo.Extensions.Scripting.ScriptLanguages.Shell;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    [DisplayName("SHCall")]
    [Description("Calls a shell script that is stored as an asset.")]
    [ScriptAlias("SHCall")]
    [ScriptNamespace(Namespaces.Linux, PreferUnqualified = true)]
    [DefaultProperty(nameof(ScriptName))]
    public sealed class SHCallOperation : ExecuteOperation, IScriptingOperation
    {
        [Required]
        [ScriptAlias("Name")]
        [DisplayName("Name")]
        [Description("The name of the script asset.")]
        [SuggestableValue(typeof(ScriptNameSuggestionProvider))]
        public string ScriptName { get; set; }
        [ScriptAlias("Arguments")]
        [Description("Arguments to pass to the script.")]
        public string Arguments { get; set; }
        [ScriptAlias("Verbose")]
        [Description("When true, additional information about staging the script is written to the debug log.")]
        public bool Verbose { get; set; }
        [Output]
        [ScriptAlias("ExitCode")]
        [DisplayName("Exit code")]
        [PlaceholderText("eg. $ScriptExitCode")]
        public int? ExitCode { get; set; }
        [Category("Logging")]
        [ScriptAlias("OutputLogLevel")]
        [DisplayName("Output log level")]
        public MessageLevel OutputLevel { get; set; } = MessageLevel.Information;
        [Category("Logging")]
        [ScriptAlias("ErrorOutputLogLevel")]
        [DisplayName("Error log level")]
        public MessageLevel ErrorLevel { get; set; } = MessageLevel.Error;

        [ScriptAlias("Parameters")]
        public IReadOnlyDictionary<string, RuntimeValue> Parameters { get; set; }
        [ScriptAlias("InputVariables")]
        public IReadOnlyDictionary<string, RuntimeValue> InputVariables { get; set; }
        [ScriptAlias("OutputVariables")]
        public IEnumerable<string> OutputVariables { get; set; }


        ScriptLanguageInfo IScriptingOperation.ScriptLanguage => new ShellScriptingLanguage();

        IReadOnlyDictionary<string, string> IScriptingOperation.EnvironmentVariables { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            using var scriptReader = SHUtil.OpenScriptAsset(this.ScriptName, this, context);
            if (scriptReader == null)
                return;

            var inputVars = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var argVars = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);

            var scriptInfo = ScriptParser.Parse<ShellScriptParser>(scriptReader);

            if (this.Parameters != null)
            {
                foreach (var paramValue in this.Parameters)
                {
                    var paramInfo = scriptInfo.Parameters.FirstOrDefault(p => string.Equals(p.Name, paramValue.Key, StringComparison.OrdinalIgnoreCase));
                    if (paramInfo == null || paramInfo.Usage == ScriptParameterUsage.InputVariable || paramInfo.Usage == ScriptParameterUsage.Default)
                        inputVars[paramValue.Key] = paramValue.Value;
                    else if (paramInfo.Usage == ScriptParameterUsage.Arguments)
                        argVars[paramValue.Key] = paramValue.Value;
                }
            }

            foreach (var p in scriptInfo.Parameters.Where(p => p.Usage == ScriptParameterUsage.Arguments && !string.IsNullOrEmpty(p.DefaultValue)))
            {
                if (!argVars.ContainsKey(p.Name))
                    argVars[p.Name] = p.DefaultValue;
            }

            var commandLineArgs = string.Empty;

            if (argVars.Count > 0 && string.IsNullOrWhiteSpace(scriptInfo.DefaultArgumentsFormat))
                this.LogWarning("Command line arguments have been specified in AhParameters, but no AhArgumentsFormat string was specified.");
            else if (!string.IsNullOrWhiteSpace(scriptInfo.DefaultArgumentsFormat))
                commandLineArgs = (await context.ExpandVariablesAsync(scriptInfo.DefaultArgumentsFormat, argVars)).AsString();

            if (!string.IsNullOrEmpty(this.Arguments))
            {
                commandLineArgs += string.IsNullOrEmpty(commandLineArgs) ? "" : $" {this.Arguments}";
            }

            if (this.InputVariables != null)
            {
                foreach (var v in this.InputVariables)
                    inputVars[v.Key] = v.Value;
            }

            if (!string.IsNullOrWhiteSpace(scriptInfo.ExecModeVariable))
                inputVars.Add(scriptInfo.ExecModeVariable.TrimStart('$'), "Execute");

            var originalOutVars = this.OutputVariables?.ToList();

            var ahOutVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (this.OutputVariables != null)
                ahOutVars.UnionWith(this.OutputVariables);

            //foreach (var c in scriptInfo.ConfigValues)
            //{
            //    addOutVar(c.ConfigKey);
            //    addOutVar(c.ConfigType);
            //    addOutVar(c.CurrentValue);
            //    addOutVar(c.DesiredValue);
            //    addOutVar(c.ValueDrifted);
            //}

            this.ExitCode = await SHUtil.ExecuteScriptAsync(context, scriptReader, this.Arguments, this, this.Verbose, this.OutputLevel, this.ErrorLevel).ConfigureAwait(false);

            //var ahStartInfo = new PythonStartInfo
            //{
            //    ScriptText = scriptText,
            //    InjectedVariables = inputVars,
            //    OutVariables = ahOutVars.ToList(),
            //    EnvironmentVariables = envVars,
            //    CommandLineArguments = commandLineArgs
            //};
            //var result = await operation.ExecutePythonScriptAsync(ahStartInfo, context);

            //var configResults = new List<ExecuteScriptResultConfigurationInfo>();

            //if (result.OutVariables != null)
            //{
            //    foreach (var c in scriptInfo.ConfigValues)
            //    {
            //        configResults.Add(
            //            new ExecuteScriptResultConfigurationInfo
            //            {
            //                ConfigKey = tryGetOutVar(c.ConfigKey),
            //                ConfigType = tryGetOutVar(c.ConfigType),
            //                CurrentConfigValue = tryGetOutVar(c.CurrentValue),
            //                DesiredConfigValue = tryGetOutVar(c.DesiredValue),
            //                DriftDetected = bool.TryParse(tryGetOutVar(c.ValueDrifted), out bool b) ? b : null
            //            }
            //        );
            //    }
            //}

            //result.Configuration = configResults;
            //if (result.OutVariables != null)
            //{
            //    foreach (var v in ahOutVars.Except(originalOutVars?.AsEnumerable() ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase))
            //        result.OutVariables.Remove(v);
            //}

            //void addOutVar(string value)
            //{
            //    if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("$"))
            //        ahOutVars.Add(value.Substring(1));
            //}

            //string tryGetOutVar(string varName)
            //{
            //    if (varName?.StartsWith("$") == true)
            //    {
            //        if (result.OutVariables.TryGetValue(varName.Substring(1), out var value))
            //            return value.AsString();
            //        else
            //            return null;
            //    }
            //    else
            //    {
            //        return AH.NullIf(varName, string.Empty);
            //    }
            //}
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var shortDesc = new RichDescription("SHCall ", new Hilite(config[nameof(this.ScriptName)]));
            var args = config[nameof(this.Arguments)];
            if (string.IsNullOrWhiteSpace(args))
            {
                return new ExtendedRichDescription(shortDesc);
            }
            else
            {
                return new ExtendedRichDescription(
                    shortDesc,
                    new RichDescription(
                        "with arguments ",
                        new Hilite(args)
                    )
                );
            }
        }
    }
}
