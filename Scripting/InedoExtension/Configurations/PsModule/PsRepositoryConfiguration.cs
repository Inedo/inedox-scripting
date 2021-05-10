using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.PowerShell;
using Inedo.Serialization;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.Configurations.PsModule
{
    [Serializable]
    [DisplayName("PowerShell Repository")]
    public class PsRepositoryConfiguration : PersistedConfiguration, IExistential
    {
        private Dictionary<string, RuntimeValue> dictionary;

        public PsRepositoryConfiguration()
        {

            if (dictionary != null && dictionary.Count > 0)
                this.dictionary = new Dictionary<string, RuntimeValue>(dictionary, StringComparer.OrdinalIgnoreCase);
        }

        [Persistent]
        [ScriptAlias("Name")]
        [DisplayName("Name")]
        [ConfigurationKey]
        [Required]
        public string Name { get; set; }

        [Persistent]
        [ScriptAlias("SourceLocation")]
        [DisplayName("Source Location")]
        [Required]
        public string SourceLocation { get; set; }

        [Persistent]
        [ScriptAlias("InstallationPolicy")]
        [DisplayName("Installation Policy")]
        [Description("Use \"Trusted\" or \"Untrusted\"")]
        public string InstallationPolicy { get; set; }

        [Persistent]
        [DefaultValue(true)]
        [ScriptAlias("Exists")]
        public bool Exists { get; set; } = true;


        #region Advanced


        [Persistent]
        [ScriptAlias("PackageManagementProvider")]
        [DisplayName("Package Management Provider")]
        public string PackageManagementProvider { get; set; }

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
                    "Ensure PowerShell Repository ",
                    new Hilite(config[nameof(Name)]),
                    " is ",
                    config[nameof(Exists)].ToString().Equals("true", StringComparison.InvariantCultureIgnoreCase) ? "registered" : "unregistered"
                );
            if (!string.IsNullOrWhiteSpace(config[nameof(InstallationPolicy)]))
                description.AppendContent($" and {config[nameof(InstallationPolicy)]}");
            description.AppendContent(".");
            return new ExtendedRichDescription(description);
        }

        public override Task<ComparisonResult> CompareAsync(PersistedConfiguration other, IOperationCollectionContext context)
        {
            if (other is not PsRepositoryConfiguration module)
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

            if ( this.SourceLocation != module.SourceLocation && !(this.SourceLocation?.Equals(module.SourceLocation, StringComparison.InvariantCultureIgnoreCase) ?? false))
            {
                differences.Add(new Difference(nameof(SourceLocation), this.SourceLocation, module.SourceLocation));
            }

            if ( this.InstallationPolicy != module.InstallationPolicy && !(this.InstallationPolicy?.Equals(module.InstallationPolicy, StringComparison.InvariantCultureIgnoreCase) ?? false))
            {
                differences.Add(new Difference(nameof(InstallationPolicy), this.InstallationPolicy, module.InstallationPolicy));
            }

            if (!string.IsNullOrWhiteSpace(this.PackageManagementProvider) && this.PackageManagementProvider != module.PackageManagementProvider && !(this.PackageManagementProvider?.Equals(module.PackageManagementProvider, StringComparison.InvariantCultureIgnoreCase) ?? false))
            {
                differences.Add(new Difference(nameof(PackageManagementProvider), this.PackageManagementProvider, module.PackageManagementProvider));
            }

            return Task.FromResult(new ComparisonResult(differences));
        }

        public static async Task ConfigureAsync(IOperationExecutionContext context, ILogSink log, PsRepositoryConfiguration template)
        {
            if (context.Simulation)
            {
                log.LogInformation("Registering PS Repository...");
                return;
            }

            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();

            if((await CollectInternalAsync(jobRunner, log, template))?.Exists ?? false)
            {
                var scriptText = "Unregister-PSRepository -Name $Name";

                if (template.Verbose)
                    scriptText += " -Verbose";


                var variables = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Name"] = template.Name
                };

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
                    log.Log(e.Level, e.Message);
                };
                await jobRunner.ExecuteJobAsync(job, context.CancellationToken);
            }

            if (template.Exists)
            {
                var scriptText = template.Exists ? $"Register-PSRepository -Name $Name -SourceLocation $SourceLocation" : "Unregister-PSRepository -Name $Name";
                
                if (!string.IsNullOrWhiteSpace(template.InstallationPolicy))
                    scriptText += " -InstallationPolicy $InstallationPolicy";
                if (!string.IsNullOrWhiteSpace(template.PackageManagementProvider))
                    scriptText += " -PackageManagementProvider $PackageManagementProvider";
                if (template.Verbose)
                    scriptText += " -Verbose";


                var variables = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Name"] = template.Name,
                    ["SourceLocation"] = template.SourceLocation,
                    ["InstallationPolicy"] = template.InstallationPolicy,
                    ["PackageManagementProvider"] = template.PackageManagementProvider
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
                    log.Log(e.Level, e.Message);
                };
                await jobRunner.ExecuteJobAsync(job, context.CancellationToken);
            }
        }

        public static async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context, ILogSink log, PsRepositoryConfiguration template)
        {
            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();
            return await CollectInternalAsync(jobRunner, log, template);
            
        }

        private static async Task<PsRepositoryConfiguration> CollectInternalAsync(IRemoteJobExecuter jobRunner, ILogSink log, PsRepositoryConfiguration template)
        {
            try
            {
                var scriptText = "$results = Get-PSRepository";
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
                        ["Name"] = template.Name
                    }
                };
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

                var repositories = (result.OutVariables["results"].AsEnumerable() ?? result.OutVariables["results"].ParseDictionary() ?? Enumerable.Empty<RuntimeValue>())
                    .Select(parseModule).Where(r => r.Name.Equals(template.Name, StringComparison.InvariantCultureIgnoreCase));

                return repositories.FirstOrDefault() ?? new PsRepositoryConfiguration { Name = template.Name, Exists = false };

                static PsRepositoryConfiguration parseModule(RuntimeValue value)
                {
                    var repository = value.AsDictionary();

                    if (repository == null)
                        return null;

                    if (!repository.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name.AsString()))
                        return null;

                    repository.TryGetValue("SourceLocation", out var sourceLocation);
                    repository.TryGetValue("InstallationPolicy", out var installationPolicy);
                    repository.TryGetValue("PackageManagementProvider", out var packageManagementProvider);

                    return new PsRepositoryConfiguration
                    {
                        Name = name.AsString(),
                        SourceLocation = sourceLocation.AsString() ?? string.Empty,
                        InstallationPolicy = installationPolicy.AsString(),
                        PackageManagementProvider = packageManagementProvider.AsString(),
                        Exists = true
                    };
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex);
                return new PsRepositoryConfiguration { Name = template.Name, Exists = false };
            }
        }
    }
}
