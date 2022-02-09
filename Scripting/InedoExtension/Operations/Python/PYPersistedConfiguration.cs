using Inedo.Extensions.Scripting.Configurations;

namespace Inedo.Extensions.Scripting.Operations.Python
{
    internal sealed class PYPersistedConfiguration : ScriptPersistedConfiguration
    {
        public PYPersistedConfiguration(ExecuteScriptResult results) : base(results)
        {
        }

        public override string ConfigurationTypeName => "PYConfig";
    }
}
