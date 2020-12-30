using System.Linq;
using Inedo.Extensions.Scripting.PowerShell;
using Inedo.Web.Editors.Operations;

namespace Inedo.Extensions.Scripting.Operations.PowerShell
{
    internal sealed class PowerShellScriptInfoProvider : ICallScriptInfoProvider
    {
        public CallScriptInfo TryLoad(string name)
        {
            var scriptName = LooselyQualifiedName.Parse(name);
            var info = PowerShellScriptInfo.TryLoad(scriptName);
            if (info == null)
                return null;

            return new CallScriptInfo(
                scriptName.ToString(),
                info.Parameters.Select(p => new CallScriptArgument
                {
                    DefaultValue = p.DefaultValue,
                    Description = p.Description,
                    IsBooleanOrSwitch = p.IsBooleanOrSwitch,
                    IsOutput = p.IsOutput,
                    Name = p.Name
                })
            );
        }
    }
}
