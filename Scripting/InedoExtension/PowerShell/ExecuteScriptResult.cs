using System.Collections.Generic;
using Inedo.ExecutionEngine;

namespace Inedo.Extensions.Scripting.PowerShell
{
    internal sealed class ExecuteScriptResult
    {
        public int? ExitCode { get; set; }
        public List<RuntimeValue> Output { get; set; }
        public Dictionary<string, RuntimeValue> OutVariables { get; set; }
        public List<ExecuteScriptResultConfigurationInfo> Configuration { get; set; }
    }
    internal sealed class ExecuteScriptResultConfigurationInfo
    {
        public string ConfigType { get; set; }
        public string ConfigKey { get; set; }
        public RuntimeValue DesiredConfigValue { get; set; }
        public RuntimeValue CurrentConfigValue { get; set; }
        public bool? DriftDetected { get; set; }
    }
}
