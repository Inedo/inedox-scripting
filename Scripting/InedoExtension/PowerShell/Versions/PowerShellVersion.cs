using System;
using System.Numerics;

namespace Inedo.Extensions.Scripting.PowerShell.Versions
{
    public abstract class PowerShellVersion : IComparable, IComparable<PowerShellVersion>, IEquatable<PowerShellVersion>
    {
        private readonly string originalString;

        protected PowerShellVersion(string originalString) => this.originalString = originalString;

        public static bool operator ==(PowerShellVersion version1, PowerShellVersion version2) => Equals(version1, version2);
        public static bool operator !=(PowerShellVersion version1, PowerShellVersion version2) => !Equals(version1, version2);
        public static bool operator <(PowerShellVersion version1, PowerShellVersion version2) => Compare(version1, version2) < 0;
        public static bool operator <=(PowerShellVersion version1, PowerShellVersion version2) => Compare(version1, version2) <= 0;
        public static bool operator >(PowerShellVersion version1, PowerShellVersion version2) => Compare(version1, version2) > 0;
        public static bool operator >=(PowerShellVersion version1, PowerShellVersion version2) => Compare(version1, version2) >= 0;

        public virtual bool IsValid => true;
        public string OriginalString => this.originalString ?? this.ToString();
        public abstract BigInteger Major { get; }
        public abstract BigInteger? Minor { get; }
        public abstract BigInteger? Patch { get; }
        public abstract string Prerelease { get; }
        public abstract string UniquePart { get; }
        public abstract string OriginalUniquePart { get; }
        public bool IsPrerelease => !string.IsNullOrEmpty(this.Prerelease);

        public static bool Equals(PowerShellVersion v1, PowerShellVersion v2)
        {
            if (ReferenceEquals(v1, v2))
                return true;
            if (ReferenceEquals(v1, null) | ReferenceEquals(v2, null))
                return false;

            return v1.Equals(v2);
        }
        public static int Compare(PowerShellVersion v1, PowerShellVersion v2)
        {
            if (ReferenceEquals(v1, v2))
                return 0;
            if (ReferenceEquals(v1, null))
                return -1;
            if (ReferenceEquals(v1, null))
                return 1;

            return v1.CompareTo(v2);
        }
        public abstract int CompareTo(PowerShellVersion other);

        public static PowerShellVersion Parse(string s) => SemVer2PowerShellVersion.TryParse(s) ?? (PowerShellVersion)LegacyPowerShellVersion.TryParse(s) ?? new InvalidPowerShellVersion(s);
        public abstract PowerShellVersion ParseSame(string s);
        public abstract PowerShellVersion TryParseSame(string s);

        int IComparable.CompareTo(object obj) => this.CompareTo(obj is PowerShellVersion v ? v : throw new ArgumentException());
        public abstract override string ToString();
        public sealed override bool Equals(object obj) => this.Equals(obj as PowerShellVersion);
        public abstract override int GetHashCode();
        public abstract bool Equals(PowerShellVersion other);
        public abstract string ToNormalizedString();
        public abstract string GetAlias();
        public abstract PowerShellVersion ToNormalizedVersion();

        protected static int LegacyAndSemVerCompare(LegacyPowerShellVersion legacy, SemVer2PowerShellVersion semver2)
        {
            if (ReferenceEquals(legacy, semver2))
                return 0;
            if (legacy is null)
                return -1;
            if (semver2 is null)
                return 1;

            if (legacy.Major != semver2.Major)
                return legacy.Major.CompareTo(semver2.Major);

            if (legacy.Minor != semver2.Minor)
                return legacy.Minor?.CompareTo(semver2.Minor) ?? -1;

            if (legacy.Patch != semver2.Patch)
                return legacy.Patch?.CompareTo(semver2.Patch) ?? -1;

            if (legacy.MyBuild > 0)
                return 1;

            return string.Compare(legacy.Prerelease ?? string.Empty, semver2.Prerelease ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        protected static bool LegacyAndSemVerEquals(LegacyPowerShellVersion legacy, SemVer2PowerShellVersion semver2)
        {
            if (ReferenceEquals(legacy, semver2))
                return true;
            if (legacy is null || semver2 is null)
                return false;

            if (legacy.MyBuild.GetValueOrDefault() != 0)
                return false;

            return legacy.Major == semver2.Major
                && legacy.Minor == semver2.Minor
                && legacy.Patch == semver2.Patch
                && string.Equals(legacy.Prerelease ?? string.Empty, semver2.Prerelease ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
