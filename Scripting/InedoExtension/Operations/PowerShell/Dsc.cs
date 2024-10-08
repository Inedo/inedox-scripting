﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.ExecutionEngine;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Scripting.Configurations.DSC;
using Inedo.Extensions.Scripting.Functions;
using Inedo.Extensions.Scripting.PowerShell;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    internal static class Dsc
    {
        private static readonly LazyRegex IsArrayPropertyRegex = new(@"^\[[^\[\]]+\[\]\]$", RegexOptions.Compiled);

        public static async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context, ILogSink log, DscConfiguration template)
        {
            if (string.IsNullOrEmpty(template.ResourceName))
            {
                log.LogError("Bad or missing DSC Resource name.");
                return null;
            }

            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();

            if (string.IsNullOrEmpty(template.PreferWindowsPowerShell))
            {
                var maybeVariable = context.TryGetVariableValue(new RuntimeVariableName("PreferWindowsPowerShell", RuntimeValueType.Scalar));
                if (maybeVariable == null)
                {
                    var maybeFunc = context.TryGetFunctionValue("PreferWindowsPowerShell");
                    if (maybeFunc == null)
                        template.PreferWindowsPowerShell = bool.TrueString;
                    else
                        template.PreferWindowsPowerShell = maybeFunc.Value.AsString();
                }
                else
                    template.PreferWindowsPowerShell = maybeVariable.Value.AsString();
            }
            
            var useWindowsPowerShell = context.GetFlagOrDefault<PreferWindowsPowerShellVariableFunction>(template.PreferWindowsPowerShell, true);
            var autoTerminateProcess = context.GetFlagOrDefault<AutoTerminatePowerShellProcessVariableFunction>(defaultValue: true);
            var propertyTypes = await GetPropertyTypesAsync(context, jobRunner, template.ResourceName, template.ModuleName, useWindowsPowerShell, log);

            var collectJob = CreateJob("Get", propertyTypes, template, useWindowsPowerShell, autoTerminateProcess);

            log.LogDebug(collectJob.ScriptText);
            collectJob.MessageLogged += (s, e) => log.Log(e.Level, e.Message);

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(collectJob, context.CancellationToken);

            var collectValues = result.Output?.FirstOrDefault().AsDictionary() ?? new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase);
            var removeKeys = collectValues.Where(p => p.Value.ValueType == RuntimeValueType.Scalar && string.IsNullOrEmpty(p.Value.AsString())).Select(p => p.Key).ToList();
            foreach (var k in removeKeys)
                collectValues.Remove(k);

            var testJob = CreateJob("Test", propertyTypes, template, useWindowsPowerShell, autoTerminateProcess);

            log.LogDebug(testJob.ScriptText);
            testJob.MessageLogged += (s, e) => log.Log(e.Level, e.Message);

            var result2 = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(testJob, context.CancellationToken);
            var output = result2.Output;

            if (output.Count == 0)
            {
                log.LogError("Invoke-DscResource did not return any values.");
                return null;
            }

            var testResult = output.FirstOrDefault();
            bool? inDesiredState = null;
            if (testResult.ValueType == RuntimeValueType.Map && testResult.AsDictionary().ContainsKey("InDesiredState"))
            {
                if (bool.TryParse(testResult.AsDictionary()["InDesiredState"].AsString(), out bool d))
                    inDesiredState = d;
            }
            else
            {
                inDesiredState = testResult.AsBoolean();
            }

            if (inDesiredState == null)
            {
                log.LogError("Invoke-DscResource did not return a boolean value or an object with an InDesiredState property.");
                return null;
            }

            return new DscConfiguration(collectValues)
            {
                ModuleName = template.ModuleName,
                ResourceName = template.ResourceName,
                ConfigurationKeyName = template.ConfigurationKeyName,
                InDesiredState = inDesiredState.Value
            };
        }
        public static async Task ConfigureAsync(IOperationExecutionContext context, ILogSink log, DscConfiguration template)
        {
            if (context.Simulation)
            {
                log.LogInformation("Invoking DscResource...");
                return;
            }

            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();
            if (string.IsNullOrEmpty(template.PreferWindowsPowerShell))
            {
                var maybeVariable = context.TryGetVariableValue(new RuntimeVariableName("PreferWindowsPowerShell", RuntimeValueType.Scalar));
                if (maybeVariable == null)
                {
                    var maybeFunc = context.TryGetFunctionValue("PreferWindowsPowerShell");
                    if (maybeFunc == null)
                        template.PreferWindowsPowerShell = bool.TrueString;
                    else
                        template.PreferWindowsPowerShell = maybeFunc.Value.AsString();
                }
                else
                    template.PreferWindowsPowerShell = maybeVariable.Value.AsString();
            }

            var useWindowsPowerShell = context.GetFlagOrDefault<PreferWindowsPowerShellVariableFunction>(template.PreferWindowsPowerShell, true);
            var autoTerminateProcess = context.GetFlagOrDefault<AutoTerminatePowerShellProcessVariableFunction>(defaultValue: true);
            var propertyTypes = await GetPropertyTypesAsync(context, jobRunner, template.ResourceName, template.ModuleName, useWindowsPowerShell, log);

            var job = CreateJob("Set", propertyTypes, template, useWindowsPowerShell, autoTerminateProcess);
            job.MessageLogged += (s, e) => log.Log(e.Level, e.Message);

            await jobRunner.ExecuteJobAsync(job, context.CancellationToken);
        }

        private static async Task<Dictionary<string, RuntimeValueType>> GetPropertyTypesAsync(IOperationExecutionContext context, IRemoteJobExecuter jobRunner, string resourceName, string moduleName, bool preferWindowsPowerShell, ILogSink log)
        {
            var job = new ExecutePowerShellJob
            {
                CollectOutput = true,
                ScriptText = @"$h = @{}
foreach($p in (Get-DscResource -Name $Name -Module $ModuleName).Properties) {
    $h[$p.Name] = $p.PropertyType
}
Write-Output $h",
                Variables = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Name"] = resourceName,
                    ["ModuleName"] = AH.CoalesceString(moduleName, "PSDesiredStateConfiguration")
                },
                PreferWindowsPowerShell = preferWindowsPowerShell,
                TerminateHostProcess = context.GetFlagOrDefault<AutoTerminatePowerShellProcessVariableFunction>(defaultValue: true)
            };

            job.MessageLogged += (s, e) => log.Log(e.Level, e.Message);

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(job, context.CancellationToken);

            var properties = result.Output.FirstOrDefault().AsDictionary();

            var types = new Dictionary<string, RuntimeValueType>(StringComparer.OrdinalIgnoreCase);
            if (properties != null)
            {
                foreach (var p in properties)
                {
                    var value = p.Value.AsString();
                    types[p.Key] = (!string.IsNullOrWhiteSpace(value) && IsArrayPropertyRegex.IsMatch(value)) ? RuntimeValueType.Vector : RuntimeValueType.Scalar;
                }
            }

            return types;
        }
        private static ExecutePowerShellJob CreateJob(string method, Dictionary<string, RuntimeValueType> propertyTypes, DscConfiguration template, bool preferWindowsPowerShell, bool terminateProcess)
        {
            var job = new ExecutePowerShellJob
            {
                CollectOutput = true,
                ScriptText = $"Invoke-DscResource -Name $Name -Method {method} -Property $Property -ModuleName $ModuleName",
                Variables = new Dictionary<string, RuntimeValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Name"] = template.ResourceName,
                    ["Property"] = new RuntimeValue(template.ToPowerShellDictionary(propertyTypes)),
                    ["ModuleName"] = AH.CoalesceString(template.ModuleName, "PSDesiredStateConfiguration")
                },
                PreferWindowsPowerShell = preferWindowsPowerShell,
                TerminateHostProcess = terminateProcess
            };

            return job;
        }
    }
}
