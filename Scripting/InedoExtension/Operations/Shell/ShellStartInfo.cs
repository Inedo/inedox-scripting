using System.Collections.Generic;
using Inedo.Diagnostics;
using Inedo.ExecutionEngine;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    internal class ShellStartInfo
    {
        public string ScriptText { get; set; }
        public IReadOnlyDictionary<string, RuntimeValue> InjectedVariables { get; set; }
        public IReadOnlyCollection<string> OutVariables { get; set; }
        public string CommandLineArguments { get; set; }
    }
}
