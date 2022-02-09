using Inedo.Extensions.Scripting.Configurations;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    internal sealed class PSPersistedConfiguration : ScriptPersistedConfiguration
    {
        public PSPersistedConfiguration(ExecuteScriptResult results) : base(results)
        {
        }

        public override string ConfigurationTypeName => "PSConfig";
    }
}
