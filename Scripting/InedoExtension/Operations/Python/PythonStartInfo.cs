﻿using System.Collections.Generic;
using Inedo.ExecutionEngine;

namespace Inedo.Extensions.Scripting.Operations.Python
{
    internal sealed class PythonStartInfo
    {
        public string ScriptText { get; set; }
        public IReadOnlyDictionary<string, RuntimeValue> InjectedVariables { get; set; }
        public IReadOnlyCollection<string> OutVariables { get; set; }
        public string CommandLineArguments { get; set; }
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; }
    }
}
