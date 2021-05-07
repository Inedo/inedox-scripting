using System;
using System.Numerics;

namespace Inedo.Extensions.Scripting.PowerShell.Versions
{
    [Serializable]
    public sealed class SemVer2PowerShellVersion : PowerShellVersion, IEquatable<SemVer2PowerShellVersion>, IComparable<SemVer2PowerShellVersion>, IComparable
    {
        private readonly ValueSemanticVersion2 semver;

        private SemVer2PowerShellVersion(string originalString, in ValueSemanticVersion2 semver)
            : base(originalString)
        {
            this.semver = semver;
        }

        public override BigInteger Major => this.semver.Major;
        public override BigInteger? Minor => this.semver.Minor;
        public override BigInteger? Patch => this.semver.Patch;
        public override string Prerelease => this.semver.Prerelease;
        public override string UniquePart => this.semver.ToString("U");
        public override string OriginalUniquePart
        {
            get
            {
                return this.semver.Build == null ? this.OriginalString : this.OriginalString.Substring(0, this.OriginalString.Length - this.semver.Build.Length - 1);
            }
        }

        public static SemVer2PowerShellVersion TryParse(string s)
        {
            if (ValueSemanticVersion2.TryParse(s, out var value))
                return new SemVer2PowerShellVersion(s, value);
            else
                return null;
        }

        public override PowerShellVersion ParseSame(string s) => Parse(s);
        public override PowerShellVersion TryParseSame(string s) => TryParse(s);

        public int CompareTo(SemVer2PowerShellVersion other) => this.semver.CompareTo(other?.semver ?? default);
        public override int CompareTo(PowerShellVersion other)
        {
            if (other is SemVer2PowerShellVersion v)
                return this.CompareTo(v);
            if (other is InvalidPowerShellVersion)
                return -1;
            return -LegacyAndSemVerCompare((LegacyPowerShellVersion)other, this);
        }
        public bool Equals(SemVer2PowerShellVersion other) => this.semver.Equals(other?.semver ?? default);
        public override bool Equals(PowerShellVersion other)
        {
            if (other is SemVer2PowerShellVersion v)
                return this.Equals(v);
            else if (other is LegacyPowerShellVersion l)
                return LegacyAndSemVerEquals(l, this);
            else
                return false;
        }
        public override int GetHashCode() => this.semver.GetHashCode();
        public override string ToString() => this.semver.ToString();
        public override string ToNormalizedString() => this.semver.ToString("U");
        public override string GetAlias()
        {
            var ver = this.semver.ToString("U");
            if (this.IsPrerelease)
                return ver.Insert(ver.IndexOf('-'), ".0");
            else
                return ver + ".0";
        }
        public override PowerShellVersion ToNormalizedVersion()
        {
            if (string.IsNullOrEmpty(this.semver.Build))
            {
                return this;
            }
            else
            {
                var ver = new ValueSemanticVersion2(this.semver.Major, this.semver.Minor, this.semver.Patch, this.semver.Prerelease);
                return new SemVer2PowerShellVersion(this.OriginalString, ver);
            }
        }
    }
}
