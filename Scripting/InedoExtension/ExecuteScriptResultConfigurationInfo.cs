using Inedo.ExecutionEngine;

namespace Inedo.Extensions.Scripting
{
    internal sealed class ExecuteScriptResultConfigurationInfo
    {
        public string ConfigType { get; set; }
        public string ConfigKey { get; set; }
        public RuntimeValue DesiredConfigValue { get; set; }
        public RuntimeValue CurrentConfigValue { get; set; }
        public bool? DriftDetected { get; set; }
    }
}
