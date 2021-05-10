using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.PowerShell;
using Inedo.Extensions.Scripting.PowerShell.Versions;
using Inedo.Serialization;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Configurations.PsModule
{
    [Serializable]
    [DisplayName("PowerShell Module")]
    public class PsModuleConfiguration : PersistedConfiguration, IExistential
    {
        private Dictionary<string, RuntimeValue> dictionary;

        public PsModuleConfiguration()
        {

            if (dictionary != null && dictionary.Count > 0)
                this.dictionary = new Dictionary<string, RuntimeValue>(dictionary, StringComparer.OrdinalIgnoreCase);
        }

        [Persistent]
        [ScriptAlias("Module")]
        [DisplayName("Module")]
        [ConfigurationKey]
        [Required]
        public string ModuleName { get; set; }

        [Persistent]
        [ScriptAlias("Version")]
        [DisplayName("Version")]
        public string Version { get; set; }
        [Persistent]
        [ScriptAlias("MinimumVersion")]
        [DisplayName("Minimum Version")]
        public string MinimumVersion { get; set; }

        [Persistent]
        [ScriptAlias("Force")]
        [DisplayName("Force")]
        [DefaultValue(false)]
        [Description("Use this to force installation to bypass the Untrusted Repository error or to force this version to install side-by-side with other versions that already exist.")]
        [IgnoreConfigurationDrift]
        public bool Force { get; set; } = false;
        
        [Persistent]
        [ScriptAlias("Repository")]
        [DisplayName("Repository Name")]
        [IgnoreConfigurationDrift]
        public string Repository { get; set; }

        [Persistent]
        [ScriptAlias("Scope")]
        [DisplayName("Scope")]
        [Description("Typically \"Local\" or \"Global\"")]
        [IgnoreConfigurationDrift]
        public string Scope { get; set; }

        [Persistent]
        [DefaultValue(true)]
        [ScriptAlias("Exists")]
        public bool Exists { get; set; } = true;

        #region Install

        [Persistent]
        [ScriptAlias("AllowClobber")]
        [DisplayName("Allow Clobber")]
        [DefaultValue(false)]
        [IgnoreConfigurationDrift]
        [Category("Install")]
        public bool AllowClobber { get; set; } = false;

        [Persistent]
        [ScriptAlias("AllowPrerelease")]
        [DisplayName("Allow Prerelease")]
        [DefaultValue(false)]
        [IgnoreConfigurationDrift]
        [Category("Install")]
        public bool AllowPrerelease { get; set; } = false;

        #endregion

        #region PS Core

        [Persistent]
        [ScriptAlias("AcceptLicense")]
        [DisplayName("Accept License")]
        [DefaultValue(false)]
        [Description("For PowerShell Core only!")]
        [IgnoreConfigurationDrift]
        [Category("PowerShell Core Only")]
        public bool AcceptLicense { get; set; } = false;

        #endregion

        #region Uninstall

        [Persistent]
        [ScriptAlias("AllVersions")]
        [DisplayName("All Versions")]
        [DefaultValue(false)]
        [IgnoreConfigurationDrift]
        [Category("Uninstall")]
        public bool AllVersions { get; set; } = false;

        #endregion

        #region Advanced

        [Persistent]
        [ScriptAlias("Parameters")]
        [ScriptAlias("Parameter")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [DisplayName("Parameters")]
        [Description(@"Additional parameters to pass to Install-Module. Example: %(DestinationPath: C:\hdars\1000.txt, Contents: test file ensured)")]
        [PlaceholderText("%(...)")]
        [IgnoreConfigurationDrift]
        [Category("Advanced")]
        public IDictionary<string, RuntimeValue> Parameters
        {
            get => new Dictionary<string, RuntimeValue>(this.dictionary ?? new Dictionary<string, RuntimeValue>(), StringComparer.OrdinalIgnoreCase);
            set
            {
                if (value == null || value.Count == 0)
                    this.dictionary = null;
                else
                    this.dictionary = new Dictionary<string, RuntimeValue>(value, StringComparer.OrdinalIgnoreCase);
            }
        }

        [Persistent]
        [DefaultValue(false)]
        [ScriptAlias("Verbose")]
        [DisplayName("Verbose")]
        [Category("Advanced")]
        [IgnoreConfigurationDrift]
        public bool Verbose { get; set; } = false;

        [Persistent]
        [DefaultValue(false)]
        [ScriptAlias("DebugLogging")]
        [DisplayName("Debug Logging")]
        [Category("Advanced")]
        [IgnoreConfigurationDrift]
        public bool DebugLogging { get; set; } = false;

        #endregion

        public static ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var description = new RichDescription(
                    "Ensure PowerShell module ",
                    new Hilite(config[nameof(ModuleName)])
                );
            var version = config[nameof(Version)];
            var minVersion = config[nameof(MinimumVersion)];
            if (!string.IsNullOrWhiteSpace(version) && string.IsNullOrWhiteSpace(minVersion))
                description.AppendContent(" version ", new Hilite(version));
            if (!string.IsNullOrWhiteSpace(minVersion))
                description.AppendContent(" minimum version ", new Hilite(minVersion));
            description.AppendContent(" is installed.");
            return new ExtendedRichDescription(description);
        }

        public override Task<ComparisonResult> CompareAsync(PersistedConfiguration other, IOperationCollectionContext context)
        {
            if (other is not PsModuleConfiguration module)
                throw new ArgumentException("Cannot compare configurations of different types.");

            var differences = new List<Difference>();

            if (!this.Exists || !module.Exists)
            {
                if (this.Exists || module.Exists)
                {
                    differences.Add(new Difference(nameof(Exists), this.Exists, module.Exists));
                }

                return Task.FromResult(new ComparisonResult(differences));
            }

            if (string.IsNullOrWhiteSpace(this.MinimumVersion) && !string.IsNullOrWhiteSpace(this.Version) && PowerShellVersion.Parse(this.Version) != PowerShellVersion.Parse(module.Version))
            {
                differences.Add(new Difference(nameof(Version), this.Version, module.Version));
            }

            if (!string.IsNullOrWhiteSpace(this.MinimumVersion) && PowerShellVersion.Parse(module.Version) < PowerShellVersion.Parse(this.MinimumVersion))
            {
                differences.Add(new Difference(nameof(MinimumVersion), this.MinimumVersion, module.Version));
            }

            return Task.FromResult(new ComparisonResult(differences));
        }

        public static async Task ConfigureAsync(IOperationExecutionContext context, ILogSink log, PsModuleConfiguration template)
        {
            if (context.Simulation)
            {
                log.LogInformation("Importing Module...");
                return;
            }

            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();
            var scriptText = template.Exists ? $"Install-Module -Name $Name" : "Uninstall-Module -Name $Name";
            if (!string.IsNullOrWhiteSpace(template.Version) && string.IsNullOrWhiteSpace(template.MinimumVersion))
                scriptText += " -RequiredVersion $Version";
            if (!string.IsNullOrWhiteSpace(template.MinimumVersion))
                scriptText += " -MinimumVersion $MinimumVersion";
            if (template.Force)
                scriptText += " -Force";
            if (template.AllVersions && !template.Exists)
                scriptText += " -AllVersions";
            if (template.AllowPrerelease && template.Exists)
                scriptText += " -AllowPrerelease";
            if (template.AllowClobber && template.Exists)
                scriptText += " -AllowClobber";
            if (template.AcceptLicense && template.Exists)
                scriptText += " -AcceptLicense";
            if (!string.IsNullOrWhiteSpace(template.Scope) && template.Exists)
                scriptText += " -Scope $Scope";
            if (!string.IsNullOrWhiteSpace(template.Repository) && template.Exists)
                scriptText += " -Repository $Repository";
            if (template.Verbose)
                scriptText += " -Verbose";

            var variables = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = template.ModuleName,
                ["Version"] = template.Version,
                ["MinimumVersion"] = template.MinimumVersion,
                ["Scope"] = template.Scope,
                ["Repository"] = template.Repository
            };

            foreach (var property in template.Parameters)
            {
                scriptText += $" -{property.Key} ${property.Key}";
                variables.Add($"${property.Key}", property.Value);
            }

            var job = new ExecutePowerShellJob
            {
                CollectOutput = true,
                VerboseLogging = template.Verbose,
                DebugLogging = template.DebugLogging,
                LogOutput = template.Verbose,
                ScriptText = scriptText,
                Variables = variables,
            
            };            
            
            log.LogDebug(job.ScriptText);
            job.MessageLogged += (s, e) =>
            {
                if (e.Message.Contains("Untrusted repository"))
                {
                    log.Log(e.Level, e.Message);
                    log.Log(MessageLevel.Error, "Repository is untrusted.  Set the repository to Trusted using \"Set-PSRepository\" or use the \"Force\" parameter.");
                }
                else if (e.Message.Contains("Are you sure you want to perform this action?"))
                {
                    log.Log(e.Level, e.Message);
                    log.Log(MessageLevel.Error, "This command requires a confirmation, please try using the \"Force\" parameter.");
                }
                else
                {
                    log.Log(e.Level, e.Message);
                }
            };
            await jobRunner.ExecuteJobAsync(job, context.CancellationToken);
        }

        public static async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context, ILogSink log, PsModuleConfiguration template)
        {
            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();
            try
            {
                var scriptText = "$results = Get-Module -ListAvailable -Name $Name";
                if (template.Verbose)
                    scriptText += " -Verbose";
                var job = new ExecutePowerShellJob
                {
                    CollectOutput = true,
                    OutVariables = new[] { "results" },
                    VerboseLogging = template.Verbose,
                    DebugLogging = template.DebugLogging,
                    LogOutput = template.Verbose,
                    ScriptText = scriptText,
                    Variables = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Name"] = template.ModuleName
                    }
                };
                log.LogDebug(job.ScriptText);
                job.MessageLogged += (s, e) => {
                    if (e.Message.Contains("No match was found for the specified"))
                    {
                        log.LogDebug(e.Message);
                    }
                    else
                    {
                        log.Log(e.Level, e.Message);
                    }
                };

                var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(job);

                var modules = (result.OutVariables["results"].AsEnumerable() ?? result.OutVariables["results"].ParseDictionary() ?? Enumerable.Empty<RuntimeValue>())
                    .Select(parseModule)
                    .Where(m => m != null && (string.IsNullOrEmpty(template.Version) || (!string.IsNullOrEmpty(template.Version) && template.Version == m.Version)));

                return modules.FirstOrDefault() ?? new PsModuleConfiguration { ModuleName = template.ModuleName, Exists = false };

                static PsModuleConfiguration parseModule(RuntimeValue value)
                {
                    var module = value.AsDictionary();

                    if (module == null)
                        return null;

                    if (!module.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name.AsString()))
                        return null;

                    module.TryGetValue("Version", out var version);

                    return new PsModuleConfiguration
                    {
                        ModuleName = name.AsString(),
                        Version = version.AsString() ?? string.Empty,
                        Exists = true
                    };
                }
            }
            catch(Exception ex)
            {
                log.LogError(ex);
                return new PsModuleConfiguration { ModuleName = template.ModuleName, Exists = false };
            }
        }
    }
}
