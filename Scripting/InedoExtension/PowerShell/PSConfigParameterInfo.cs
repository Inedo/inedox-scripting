using System;
using System.Collections.Generic;
using System.Linq;
using Inedo.Diagnostics;

namespace Inedo.Extensions.Scripting.PowerShell
{
    public sealed class PSConfigParameterInfo
    {
        public PSConfigParameterInfo(Dictionary<string, string> dic) => this.dic = dic;

        private readonly Dictionary<string, string> dic;
        
        private string val(string key) => this.dic.TryGetValue(key, out string value) ? value : null;

        public string ConfigType => this.val("AHCONFIGTYPE");
        public string ConfigKey => this.val("AHCONFIGKEY");
        public string DesiredValue => this.val("AHDESIREDVALUE");
        public string CurrentValue => this.val("AHCURRENTVALUE");
        public string ValueDrifted => this.val("AHVALUEDRIFTED");

        public static IEnumerable<PSConfigParameterInfo> FromDocumentationBlocks(ILookup<string, (string param, string content)> docBlocks)
        {
            var names = new[] { "AHCONFIGTYPE", "AHCONFIGKEY", "AHDESIREDVALUE", "AHCURRENTVALUE", "AHVALUEDRIFTED" };
            var numParams = docBlocks
                .Where(l => names.Contains(l.Key, StringComparer.OrdinalIgnoreCase))
                .Max(l => l.Count());

            for (int i = 0; i < numParams; i++)
            {
                var paramValues = from name in names
                                  let paramBlock = docBlocks[name]
                                  where i < paramBlock?.Count()
                                  let param = paramBlock.Skip(i).First()
                                  where !string.IsNullOrEmpty(param.content)
                                  select (name, param.content);
                yield return new PSConfigParameterInfo(paramValues.ToDictionary(k => k.Item1, v => v.content));
            }

        }
    }
}
