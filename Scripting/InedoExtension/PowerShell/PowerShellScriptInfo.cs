using System;
using System.Collections.ObjectModel;

namespace Inedo.Extensions.Scripting.PowerShell
{
    /// <summary>
    /// Contains metadata about a script.
    /// </summary>
    [Serializable]
    public sealed partial class PowerShellScriptInfo : IEquatable<PowerShellScriptInfo>, IComparable<PowerShellScriptInfo>
    {
        public string Name { get; private set; }
        public string Description { get; private set; }
        public ReadOnlyCollection<PowerShellParameterInfo> Parameters { get; private set; }
        public ReadOnlyCollection<PSConfigParameterInfo> ConfigParameters { get; private set; }
       
        public string ExecutionModeVariableName { get; private set; }

        public override string ToString() => $"{this.Name ?? "??"}({(this.Parameters == null ? "??" : string.Join(", ", this.Parameters))})";
        public bool Equals(PowerShellScriptInfo other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other is null)
                return false;

            return StringComparer.OrdinalIgnoreCase.Equals(this.Name, other.Name);
        }
        public override bool Equals(object obj) => this.Equals(obj as PowerShellScriptInfo);
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(this.Name ?? string.Empty);
        public int CompareTo(PowerShellScriptInfo other)
        {
            if (ReferenceEquals(this, other))
                return 0;
            if (other is null)
                return 1;

            return StringComparer.OrdinalIgnoreCase.Compare(this.Name, other.Name);
        }
    }
}
