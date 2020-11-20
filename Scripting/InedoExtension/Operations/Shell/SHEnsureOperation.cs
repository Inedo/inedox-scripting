using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    [DisplayName("SHEnsure")]
    [Description("Uses two shell scripts to Collect, and then Ensure a configuration about a server.")]
    [ScriptAlias("SHEnsure")]
    [ScriptNamespace(Namespaces.Linux, PreferUnqualified = true)]
    [Note("The Key is a unique string per server, and having multiple operations attempt to use the same key will yield in unpredictable behavior.")]
    public sealed class SHEnsureOperation : EnsureOperation
    {
        [Required]
        [ScriptAlias("Key")]
        [DisplayName("Configuration key")]
        public string ConfigurationKey { get; set; }
        [Required]
        [ScriptAlias("Value")]
        [DisplayName("Expected value")]
        public string ExpectedValue { get; set; }
        [ScriptAlias("Collect")]
        [DisplayName("Collection script")]
        [Description("The output of this shell script will be used to collect the current configuration of the server.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [Category("Collect")]
        public string CollectScript { get; set; }
        [ScriptAlias("Configure")]
        [DisplayName("Configure script")]
        [Description("This shell script is executed if the configuration gathered using the collection script does not match the stored configuration.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [Category("Configure")]
        public string ConfigureScript { get; set; }
        [ScriptAlias("CollectScript")]
        [DisplayName("Collection script asset")]
        [Description("The name of a shell script asset to use for collection. The output of this script will be used to collect the current configuration of the server.")]
        [SuggestableValue(typeof(ScriptNameSuggestionProvider))]
        [Category("Collect")]
        public string CollectScriptAsset { get; set; }
        [ScriptAlias("ConfigureScript")]
        [DisplayName("Configuration script asset")]
        [Description("The name of a shell script asset to use for configuration. This script is executed if the configuration gathered using the collection script does not match the stored configuration.")]
        [SuggestableValue(typeof(ScriptNameSuggestionProvider))]
        [Category("Configure")]
        public string ConfigureScriptAsset { get; set; }
        [ScriptAlias("UseExitCode")]
        [DisplayName("Use exit code")]
        [Description("When set, the exit/return code of the script will be used instead of the output stream for collection.")]
        [Category("Collect")]
        public bool UseExitCode { get; set; }
        [ScriptAlias("Verbose")]
        [Description("When true, additional information about staging the script is written to the debug log.")]
        [Category("Logging")]
        public bool Verbose { get; set; }
        [ScriptAlias("CollectScriptArgs")]
        [DisplayName("Collection script arguments")]
        [Description("Arguments to pass to the collect script.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [Category("Collect")]
        public string CollectScriptArgs { get; set; }
        [ScriptAlias("ConfigureScriptArgs")]
        [DisplayName("Configure script arguments")]
        [Description("Arguments to pass to the configure script.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [Category("Configure")]
        public string ConfigureScriptArgs { get; set; }

        public override PersistedConfiguration GetConfigurationTemplate()
        {
            return new KeyValueConfiguration
            {
                Key = this.ConfigurationKey,
                Value = this.ExpectedValue
            };
        }
        public override async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context)
        {
            if (!this.ValidateConfiguration())
                return null;

            int? exitCode;
            var output = new List<string>();
            using (var scriptReader = this.OpenCollectScript(context))
            {
                exitCode = await SHUtil.ExecuteScriptAsync(
                    context,
                    scriptReader,
                    !string.IsNullOrWhiteSpace(this.CollectScriptAsset) ? this.CollectScriptArgs : null,
                    this,
                    this.Verbose,
                    !this.UseExitCode ? (Action<string>)
                        (s =>
                        {
                            if (!string.IsNullOrWhiteSpace(s))
                                output.Add(s);
                        }) : null
                );
            }

            return new KeyValueConfiguration
            {
                Key = this.ConfigurationKey,
                Value = this.UseExitCode ? exitCode?.ToString() : string.Join(Environment.NewLine, output)
            };
        }

        public override async Task ConfigureAsync(IOperationExecutionContext context)
        {
            if (!this.ValidateConfiguration())
                return;

            using (var scriptReader = this.OpenConfigureScript(context))
            {
                await SHUtil.ExecuteScriptAsync(
                    context,
                    scriptReader,
                    !string.IsNullOrWhiteSpace(this.ConfigureScriptAsset) ? this.ConfigureScriptArgs : null,
                    this,
                    this.Verbose
                );
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config[nameof(this.CollectScript)]))
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
                        new Hilite(config[nameof(this.CollectScript)])
                    ),
                    new RichDescription("using bash")
                );
            }
            else if (!string.IsNullOrWhiteSpace(config[nameof(this.CollectScriptAsset)]))
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
                        new Hilite(config[nameof(this.CollectScriptAsset)])
                    ),
                    new RichDescription("using bash script asset")
                );
            }
            else
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Ensure ",
                        new Hilite("bash")
                    )
                );
            }
        }

        private TextReader OpenCollectScript(IOperationExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(this.CollectScriptAsset))
                return SHUtil.OpenScriptAsset(this.CollectScriptAsset, this, context);
            else
                return new StringReader(this.CollectScript);
        }
        private TextReader OpenConfigureScript(IOperationExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(this.ConfigureScriptAsset))
                return SHUtil.OpenScriptAsset(this.ConfigureScriptAsset, this, context);
            else
                return new StringReader(this.ConfigureScript);
        }
        private bool ValidateConfiguration()
        {
            bool valid = true;

            if (string.IsNullOrWhiteSpace(this.CollectScript) && string.IsNullOrWhiteSpace(this.CollectScriptAsset))
            {
                this.LogError("Collect script missing. Specify a value for either \"Collect\" or \"CollectScript\".");
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(this.ConfigureScript) && string.IsNullOrWhiteSpace(this.ConfigureScriptAsset))
            {
                this.LogError("Configure script missing. Specify a value for either \"Configure\" or \"ConfigureScript\".");
                valid = false;
            }

            if (!string.IsNullOrWhiteSpace(this.CollectScript) && !string.IsNullOrWhiteSpace(this.CollectScriptAsset))
            {
                this.LogError("Values are specified for both \"Collect\" and \"CollectScript\". Specify only one of each.");
                valid = false;
            }

            if (!string.IsNullOrWhiteSpace(this.ConfigureScript) && !string.IsNullOrWhiteSpace(this.ConfigureScriptAsset))
            {
                this.LogError("Values are specified for both \"Configure\" and \"ConfigureScript\". Specify only one of each.");
                valid = false;
            }

            return valid;
        }
    }
}
