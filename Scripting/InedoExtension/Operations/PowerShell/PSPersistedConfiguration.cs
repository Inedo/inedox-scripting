using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.PowerShell;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    internal sealed class PSPersistedConfiguration : PersistedConfiguration
    {
        private readonly ExecuteScriptResult scriptResults;
        private readonly IReadOnlyList<Difference> comparisonResults;
        public PSPersistedConfiguration(ExecuteScriptResult results)
        {
            this.scriptResults = results;
            this.comparisonResults = compare().ToList();

            IEnumerable<Difference> compare()
            {
                if (results.Configuration == null)
                    yield break;
                foreach (var config in results.Configuration)
                {
                    if (config.DriftDetected == false)
                    {
                        yield return null;
                        continue;
                    }

                    var actual = config.CurrentConfigValue.AsString();
                    var desired = config.DesiredConfigValue.AsString();
                    if (config.DriftDetected != true && string.Equals(actual, desired, System.StringComparison.OrdinalIgnoreCase))
                    {
                        yield return null;
                        continue;
                    }

                    yield return new Difference(config.ConfigKey, desired, actual);
                }
            }
        }

        public override async Task PersistAsync(ConfigurationPersistenceContext context)
        {
            if (this.scriptResults.Configuration != null)

                foreach (var config in this.scriptResults.Configuration)
                {
                    var keyValueConfig = new KeyValueConfiguration
                    {
                        Type = AH.CoalesceString(config.ConfigType, "PSConfig"),
                        Key = config.ConfigKey,
                        Value = config.CurrentConfigValue.ToString()
                    };
                    await keyValueConfig.PersistAsync(context);
                }
        }
        public override Task<ComparisonResult> CompareAsync(PersistedConfiguration other, IOperationCollectionContext context)
        {
            var diffs = this.comparisonResults.Where(d => d != null);
            if (diffs.Any())
                return Task.FromResult(new ComparisonResult(diffs));
            else
                return Task.FromResult(ComparisonResult.Identical);
        }
        public async Task StoreConfigurationStatusAsync(ConfigurationPersistenceContext context)
        {
            for (int i = 0; i < this.scriptResults.Configuration?.Count; i++)
            {
                await context.SetConfigurationStatusAsync(
                    AH.CoalesceString(this.scriptResults.Configuration[i].ConfigType, "PSConfig"),
                    this.scriptResults.Configuration[i].ConfigKey,
                    this.comparisonResults[i] == null ? ConfigurationStatus.Current : ConfigurationStatus.Drifted);
            }
        }
    }
}
