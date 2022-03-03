using Inedo.Extensions.Scripting.Configurations;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    internal sealed class SHPersistedConfiguration : ScriptPersistedConfiguration
    {
        public SHPersistedConfiguration(ExecuteScriptResult results) : base(results)
        {
        }

        public override string ConfigurationTypeName => "SHConfig";
    }
}
