using System.Collections.Generic;
using Inedo.Agents;

namespace Inedo.Extensions.Scripting
{
    internal static class Shims
    {
        public static RemoteProcessStartInfo WithEnvironmentVariables(this RemoteProcessStartInfo startInfo, IEnumerable<KeyValuePair<string, string>> environmentVariables)
        {
            if (environmentVariables != null)
            {
                foreach (var var in environmentVariables)
                    startInfo.EnvironmentVariables[var.Key] = var.Value;
            }

            return startInfo;
        }
    }
}
