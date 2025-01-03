﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.ExecutionEngine;
using Inedo.Extensibility.Operations;
using Inedo.IO;
using Microsoft.Win32;

namespace Inedo.Extensions.Scripting.PowerShell
{
    internal sealed class PowerShellScriptRunner : ILogger, IDisposable
    {
        private const string OutVarPrefix = "!INEDO_VAR!";
        private const string PowerShellProcessIdPrefix = "!INEDO_PS_PID!";

        private static readonly LazyRegex TypeCastRegex = new(@"^\[type::(?<1>[^\]]+)\](?<2>.+)$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        private static readonly LazyRegex VariableRegex = new(@"(?>\$(?<1>[a-zA-Z0-9_]+)|\${(?<2>[^}]+)})", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        private readonly InedoPSHost pshost = new();
        private readonly Lazy<Runspace> runspaceFactory;
        private readonly HashSet<int> hostProcessIds = [];
        private bool disposed;

        public PowerShellScriptRunner()
        {
            this.pshost.MessageLogged += (s, e) => this.Log(e.Level, e.Message);
            this.runspaceFactory = new Lazy<Runspace>(this.InitializeRunspace);
        }

        public event EventHandler<PowerShellOutputEventArgs> OutputReceived;
        public event EventHandler<LogMessageEventArgs> MessageLogged;
        public event EventHandler<PSProgressEventArgs> ProgressUpdate;

        public Runspace Runspace => this.runspaceFactory.Value;
        public bool DebugLogging { get; set; }
        public bool VerboseLogging { get; set; }
        public bool PreferWindowsPowerShell { get; set; }
        public bool TerminateHostProcess { get; set; }

        public static Dictionary<string, RuntimeValue> ExtractVariables(string script, IOperationExecutionContext context)
        {
            var vars = ExtractVariablesInternal(script);
            var results = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var var in vars)
            {
                if (RuntimeVariableName.IsLegalVariableName(var))
                {
                    var varName = new RuntimeVariableName(var, RuntimeValueType.Scalar);
                    var varValue = context.TryGetVariableValue(varName) ?? TryGetFunctionValue(varName, context);
                    if (varValue.HasValue)
                        results[var] = varValue.Value;
                }
            }

            return results;
        }
        public static object ConvertToPSValue(RuntimeValue value)
        {
            if (value.ValueType == RuntimeValueType.Scalar)
            {
                var s = value.AsString() ?? string.Empty;
                if (s.StartsWith(Functions.PsCredentialVariableFunction.Prefix))
                    return Functions.PsCredentialVariableFunction.Deserialize(s.Substring(Functions.PsCredentialVariableFunction.Prefix.Length));

                var match = TypeCastRegex.Match(s);
                if (match.Success)
                {
                    var v = match.Groups[2].Value;
                    switch (match.Groups[1].Value.ToLowerInvariant())
                    {
                        case "int":
                        case "int32":
                        case "sint32":
                        case "system.int32":
                            if (int.TryParse(v, out var i))
                                return i;
                            break;
                        case "uint":
                        case "uint32":
                        case "system.uint32":
                            if (uint.TryParse(v, out var u))
                                return u;
                            break;
                        case "bool":
                        case "boolean":
                        case "system.boolean":
                            if (bool.TryParse(v, out var b))
                                return b;
                            break;
                        case "long":
                        case "int64":
                        case "system.int64":
                            if (long.TryParse(v, out var l))
                                return l;
                            break;
                        case "ulong":
                        case "uint64":
                        case "system.uint64":
                            if (ulong.TryParse(v, out var ul))
                                return ul;
                            break;
                        case "string":
                        case "system.string":
                            return v;
                        case "float":
                        case "single":
                        case "system.single":
                            if (float.TryParse(v, out var f))
                                return f;
                            break;
                        case "double":
                        case "system.double":
                            if (double.TryParse(v, out var d))
                                return d;
                            break;
                        case "decimal":
                        case "system.decimal":
                            if (decimal.TryParse(v, out var de))
                                return de;
                            break;
                        case "byte":
                        case "uint8":
                        case "system.byte":
                            if (byte.TryParse(v, out var bt))
                                return bt;
                            break;
                        case "int8":
                        case "sbyte":
                        case "system.sbyte":
                            if (sbyte.TryParse(v, out var sbt))
                                return sbt;
                            break;
                        case "short":
                        case "int16":
                        case "system.int16":
                            if (short.TryParse(v, out var sh))
                                return sh;
                            break;
                        case "ushort":
                        case "uint16":
                        case "system.uint16":
                            if (ushort.TryParse(v, out var ush))
                                return ush;
                            break;
                    }
                }

                if (int.TryParse(s, out int i2))
                    return i2;

                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
                    return false;

                return s;
            }
            else if (value.ValueType == RuntimeValueType.Vector)
            {
                return value.AsEnumerable().Select(ConvertToPSValue).ToArray();
            }
            else if (value.ValueType == RuntimeValueType.Map)
            {
                var hashTable = new Hashtable();
                foreach (var pair in value.AsDictionary())
                    hashTable[pair.Key] = ConvertToPSValue(pair.Value);

                return hashTable;
            }
            else
            {
                return null;
            }
        }

        public async Task<int?> RunAsync(string script, Dictionary<string, RuntimeValue> variables = null, Dictionary<string, RuntimeValue> parameters = null, Dictionary<string, RuntimeValue> outVariables = null, string workingDirectory = null, CancellationToken cancellationToken = default)
        {
            variables ??= new Dictionary<string, RuntimeValue>();
            parameters ??= new Dictionary<string, RuntimeValue>();
            outVariables ??= new Dictionary<string, RuntimeValue>();

            var outVarsSet = new HashSet<string>();

            var runspace = this.Runspace;

            var powerShell = System.Management.Automation.PowerShell.Create();
            powerShell.Runspace = runspace;

            foreach (var var in variables)
            {
                this.LogDebug($"Importing {var.Key}...");
                runspace.SessionStateProxy.SetVariable(var.Key, ConvertToPSValue(var.Value));
            }

            if (this.DebugLogging)
                runspace.SessionStateProxy.SetVariable("DebugPreference", "Continue");

            if (this.VerboseLogging)
                runspace.SessionStateProxy.SetVariable("VerbosePreference", "Continue");

            var output = new PSDataCollection<PSObject>();
            output.DataAdded +=
                (s, e) =>
                {
                    var obj = output[e.Index];
                    if (obj?.BaseObject is string value)
                    {
                        if (value.StartsWith(OutVarPrefix) && value.Length > OutVarPrefix.Length)
                        {
                            var span = value.AsSpan(OutVarPrefix.Length);
                            int varNameLength = span.IndexOf('!');
                            if (varNameLength > 0)
                            {
                                var varName = span[0..varNameLength].ToString();
                                var varValue = ParseJson(value.AsMemory(OutVarPrefix.Length + varNameLength + 1));
                                lock (outVariables)
                                {
                                    outVarsSet.Add(varName);
                                    outVariables[varName] = varValue;
                                }
                            }
                        }
                        else if (value.StartsWith(PowerShellProcessIdPrefix) && value.Length > PowerShellProcessIdPrefix.Length)
                        {
                            if (int.TryParse(value.AsSpan(PowerShellProcessIdPrefix.Length), out int pid))
                            {
                                lock (this.hostProcessIds)
                                    this.hostProcessIds.Add(pid);
                            }
                        }
                        else
                        {
                            this.OnOutputReceived(obj);
                        }
                    }
                    else
                    {
                        this.OnOutputReceived(obj);
                    }
                };

            powerShell.Streams.Progress.DataAdded += (s, e) => this.OnProgressUpdate(powerShell.Streams.Progress[e.Index]);

            powerShell.Streams.AttachLogging(this);

            if (this.TerminateHostProcess)
            {
                powerShell.AddScript($"\"{PowerShellProcessIdPrefix}$PID\"");
                powerShell.AddStatement();
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                DirectoryEx.Create(workingDirectory);
                powerShell.AddCommand("Set-Location");
                powerShell.AddParameter("Path", workingDirectory);
                powerShell.AddStatement();
            }

            powerShell.AddScript(script);

            foreach (var p in parameters)
            {
                this.LogDebug($"Assigning parameter {p.Key}...");
                powerShell.AddParameter(p.Key, ConvertToPSValue(p.Value));
            }

            if (outVariables.Count > 0)
            {
                foreach (var var in outVariables.Keys)
                {
                    powerShell.AddStatement();
                    powerShell.AddScript($"Write-Output \"{OutVarPrefix}{var}!$(ConvertTo-Json -InputObject ${{{var}}} -Compress)\"");
                }
            }

            int? exitCode = null;
            this.pshost.ShouldExit += handleShouldExit;
            using (var registration = cancellationToken.Register(powerShell.Stop))
            {
                try
                {
                    await Task.Factory.FromAsync(powerShell.BeginInvoke((PSDataCollection<PSObject>)null, output), powerShell.EndInvoke);

                    foreach (var var in outVariables.Keys.ToList())
                    {
                        if (!outVarsSet.Contains(var))
                            outVariables[var] = PSUtil.ToRuntimeValue(unwrapReference(powerShell.Runspace.SessionStateProxy.GetVariable(var)));
                    }
                }
                finally
                {
                    this.pshost.ShouldExit -= handleShouldExit;
                }
            }

            void handleShouldExit(object s, ShouldExitEventArgs e) => exitCode = e.ExitCode;

            static object unwrapReference(object value) => value is PSReference reference ? reference.Value : value;

            return exitCode;
        }
        public void Log(MessageLevel logLevel, string message) => this.MessageLogged?.Invoke(this, new LogMessageEventArgs(logLevel, message));
        public void Dispose()
        {
            if (!this.disposed && this.runspaceFactory.IsValueCreated)
            {
                try
                {
                    this.runspaceFactory.Value.Close();
                    this.runspaceFactory.Value.Dispose();
                }
                catch
                {
                }

                if (this.TerminateHostProcess)
                {
                    int myPid = Environment.ProcessId;

                    foreach (int pid in this.hostProcessIds)
                    {
                        if (pid != myPid)
                        {
                            try
                            {
                                using var process = Process.GetProcessById(pid);
                                process.Kill();
                            }
                            catch (Exception ex)
                            {
                                this.LogWarning($"Failed to terminate PowerShell process PID={pid}: {ex.Message}");
                            }
                        }
                    }
                }

                this.disposed = true;
            }
        }

        private Runspace InitializeRunspace()
        {
            Runspace runspace = null;

            if (this.PreferWindowsPowerShell)
            {
                if (HasWindowsPowerShell51())
                {
                    runspace = RunspaceFactory.CreateOutOfProcessRunspace(null, new PowerShellProcessInstance(new Version(5, 1), null, null, false));
                    this.LogDebug("Using Windows PowerShell 5.1...");
                }
                else if (OperatingSystem.IsWindows())
                {
                    this.LogWarning("PreferWindowsPowerShell is specified, but Windows PowerShell 5.1 is not available on this server. Falling back to PSCore.");
                }
                else
                {
                    this.LogDebug("Using PowerShell Core...");
                }
            }
            else
            {
                this.LogDebug("Using PowerShell Core...");
            }

            runspace ??= RunspaceFactory.CreateRunspace(this.pshost);
            runspace.Open();
            return runspace;
        }

        private static bool HasWindowsPowerShell51()
        {
            if (OperatingSystem.IsWindows())
            {
                // this is how System.Management.Automation checks for Windows PowerShell
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PowerShell\3\PowerShellEngine");
                return key.GetValue("PowerShellVersion") is string s && s.StartsWith("5.1");
            }
            else
            {
                return false;
            }
        }

        private static RuntimeValue? TryGetFunctionValue(RuntimeVariableName functionName, IOperationExecutionContext context)
        {
            try
            {
                return context.TryGetFunctionValue(functionName.ToString());
            }
            catch
            {
                return null;
            }
        }
        private static IEnumerable<string> ExtractVariablesInternal(string script)
        {
            var tokens = PSParser.Tokenize(script, out var errors);
            if (tokens == null)
                return Enumerable.Empty<string>();

            var vars = tokens
                .Where(v => v.Type == PSTokenType.Variable)
                .Select(v => v.Content)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var strings = tokens
                .Where(t => t.Type == PSTokenType.String)
                .Select(t => t.Content);

            foreach (var s in strings)
            {
                var matches = VariableRegex.Matches(s);
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        if (match.Groups[1].Success)
                            vars.Add(match.Groups[1].Value);
                        else if (match.Groups[2].Success)
                            vars.Add(match.Groups[2].Value);
                    }
                }
            }

            return vars;
        }
        private static RuntimeValue ParseJson(ReadOnlyMemory<char> json)
        {
            if (json.IsEmpty)
                return default;

            using var doc = JsonDocument.Parse(json);
            return convertElement(doc.RootElement);

            static RuntimeValue convertElement(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                        return element.ToString();

                    case JsonValueKind.Object:
                        {
                            var dict = new Dictionary<string, RuntimeValue>();
                            foreach (var prop in element.EnumerateObject())
                                dict.Add(prop.Name, convertElement(prop.Value));

                            if (dict.Count == 1 && dict.TryGetValue("Value", out var nestedValue))
                                return nestedValue;

                            return new RuntimeValue(dict);
                        }

                    case JsonValueKind.Array:
                        {
                            var list = new List<RuntimeValue>();
                            foreach (var item in element.EnumerateArray())
                                list.Add(convertElement(item));

                            return new RuntimeValue(list);
                        }

                    default:
                        return string.Empty;
                }
            }
        }

        private void OnOutputReceived(PSObject obj) => this.OutputReceived?.Invoke(this, new PowerShellOutputEventArgs(obj));
        private void OnProgressUpdate(ProgressRecord p) => this.ProgressUpdate?.Invoke(this, new PSProgressEventArgs(p.PercentComplete, p.Activity ?? string.Empty));
    }
}
