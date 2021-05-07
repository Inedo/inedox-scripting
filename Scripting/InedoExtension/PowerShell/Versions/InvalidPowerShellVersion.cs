using System;
using System.Numerics;

namespace Inedo.Extensions.Scripting.PowerShell.Versions
{
    internal sealed class InvalidPowerShellVersion : PowerShellVersion
    {
        public InvalidPowerShellVersion(string originalString) : base(originalString)
        {
        }

        public override bool IsValid => false;
        public override BigInteger Major => 0;
        public override BigInteger? Minor => null;
        public override BigInteger? Patch => null;
        public override string Prerelease => null;
        public override string UniquePart => this.OriginalString;
        public override string OriginalUniquePart => this.OriginalString;

        public override int CompareTo(PowerShellVersion other)
        {
            if (other is InvalidPowerShellVersion)
                return string.Compare(this.OriginalString, other.OriginalString, StringComparison.OrdinalIgnoreCase);

            return 1;
        }
        public override bool Equals(PowerShellVersion other) => other is InvalidPowerShellVersion && string.Equals(this.OriginalString, other.OriginalString, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(this.OriginalString);
        public override string ToString() => this.OriginalString;
        public override string ToNormalizedString() => this.OriginalString;
        public override PowerShellVersion ParseSame(string s) => new InvalidPowerShellVersion(s);
        public override PowerShellVersion TryParseSame(string s) => new InvalidPowerShellVersion(s);
        public override string GetAlias() => null;
        public override PowerShellVersion ToNormalizedVersion() => this;
    }
}
