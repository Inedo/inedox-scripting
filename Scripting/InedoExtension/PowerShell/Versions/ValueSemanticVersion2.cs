using System;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Inedo.Extensions.Scripting.PowerShell.Versions
{
    internal readonly struct ValueSemanticVersion2 : IEquatable<ValueSemanticVersion2>, IComparable<ValueSemanticVersion2>, IComparable
    {
        private static readonly char[] Dot = new[] { '.' };
        private static readonly Regex SemanticVersionRegex = new Regex(
            @"^(?<1>[0-9]+)\.(?<2>[0-9]+)\.(?<3>[0-9]+)(-(?<4>[0-9a-zA-Z\.-]+))?(\+(?<5>[0-9a-zA-Z\.-]+))?$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture
        );

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticVersion2"/> class.
        /// </summary>
        /// <param name="major">The major version component.</param>
        /// <param name="minor">The minor version component.</param>
        /// <param name="patch">The patch version component.</param>
        /// <param name="prerelease">The prerelease version component, or null if not applicable.</param>
        /// <param name="build">The build information, or null if not applicable.</param>
        public ValueSemanticVersion2(BigInteger major, BigInteger minor, BigInteger patch, string prerelease = null, string build = null)
        {
            this.Major = major;
            this.Minor = minor;
            this.Patch = patch;
            this.Prerelease = AH.NullIf(prerelease, string.Empty);
            this.Build = AH.NullIf(build, string.Empty);
        }

        public static bool operator ==(in ValueSemanticVersion2 a, in ValueSemanticVersion2 b) => Equals(a, b);
        public static bool operator !=(in ValueSemanticVersion2 a, in ValueSemanticVersion2 b) => !Equals(a, b);
        public static bool operator <(in ValueSemanticVersion2 a, in ValueSemanticVersion2 b) => Compare(a, b) < 0;
        public static bool operator >(in ValueSemanticVersion2 a, in ValueSemanticVersion2 b) => Compare(a, b) > 0;
        public static bool operator <=(in ValueSemanticVersion2 a, in ValueSemanticVersion2 b) => Compare(a, b) <= 0;
        public static bool operator >=(in ValueSemanticVersion2 a, in ValueSemanticVersion2 b) => Compare(a, b) >= 0;

        /// <summary>
        /// Gets the major component of the version number.
        /// </summary>
        public BigInteger Major { get; }
        /// <summary>
        /// Gets the minor component of the version number.
        /// </summary>
        public BigInteger Minor { get; }
        /// <summary>
        /// Gets the patch component of the version number.
        /// </summary>
        public BigInteger Patch { get; }
        /// <summary>
        /// Gets the prerelease component of the version number, or null if not applicable.
        /// </summary>
        public string Prerelease { get; }
        /// <summary>
        /// Gets the build component of the version number, or null if not applicable.
        /// </summary>
        public string Build { get; }

        public static ValueSemanticVersion2 Parse(string s)
        {
            ParseInternal(s, out var error, out var value);
            if (error != null)
                throw new FormatException(error);

            return value;
        }
        public static bool TryParse(string s, out ValueSemanticVersion2 value)
        {
            ParseInternal(s, out var error, out value);
            return error == null;
        }

        public static bool Equals(in ValueSemanticVersion2 a, in ValueSemanticVersion2 b)
        {
            return a.Major == b.Major
                && a.Minor == b.Minor
                && a.Patch == b.Patch
                && string.Equals(a.Prerelease, b.Prerelease, StringComparison.OrdinalIgnoreCase);
        }
        public static int Compare(in ValueSemanticVersion2 a, in ValueSemanticVersion2 b)
        {
            int diff = a.Major.CompareTo(b.Major);
            if (diff != 0)
                return diff;

            diff = a.Minor.CompareTo(b.Minor);
            if (diff != 0)
                return diff;

            diff = a.Patch.CompareTo(b.Patch);
            if (diff != 0)
                return diff;

            if (a.Prerelease == null && b.Prerelease == null)
                return 0;
            if (a.Prerelease == null && b.Prerelease != null)
                return 1;
            if (a.Prerelease != null && b.Prerelease == null)
                return -1;

            var prereleaseA = a.Prerelease.Split(Dot);
            var prereleaseB = b.Prerelease.Split(Dot);

            int index = 0;
            while (true)
            {
                var aIdentifier = index < prereleaseA.Length ? prereleaseA[index] : null;
                var bIdentifier = index < prereleaseB.Length ? prereleaseB[index] : null;

                if (aIdentifier == null && bIdentifier == null)
                    break;
                if (aIdentifier == null)
                    return -1;
                if (bIdentifier == null)
                    return 1;

                bool aIntParsed = BigInteger.TryParse(aIdentifier, out var aInt);
                bool bIntParsed = BigInteger.TryParse(bIdentifier, out var bInt);

                if (aIntParsed && bIntParsed)
                {
                    diff = aInt.CompareTo(bInt);
                    if (diff != 0)
                        return diff;
                }
                else if (!aIntParsed && bIntParsed)
                {
                    return 1;
                }
                else if (aIntParsed && !bIntParsed)
                {
                    return -1;
                }
                else
                {
                    diff = string.Compare(aIdentifier, bIdentifier);
                    if (diff != 0)
                        return diff;
                }

                index++;
            }

            return 0;
        }

        public bool Equals(ValueSemanticVersion2 other) => Equals(this, other);
        public override bool Equals(object obj) => obj is ValueSemanticVersion2 v ? Equals(this, v) : false;
        public override int GetHashCode()
        {
            return unchecked((this.Major.GetHashCode() << 24) ^
                (this.Minor.GetHashCode() << 16) ^
                (this.Patch.GetHashCode() << 8) ^
                StringComparer.Ordinal.GetHashCode(this.Prerelease ?? string.Empty));
        }
        public override string ToString() => this.ToString("G");
        public string ToString(string format)
        {
            if (format != "U" && format != "G")
                throw new ArgumentException();

            var buffer = new StringBuilder(50);
            buffer.Append(this.Major);
            buffer.Append('.');
            buffer.Append(this.Minor);
            buffer.Append('.');
            buffer.Append(this.Patch);

            if (this.Prerelease != null)
            {
                buffer.Append('-');
                buffer.Append(this.Prerelease);
            }

            if (this.Build != null && format != "U")
            {
                buffer.Append('+');
                buffer.Append(this.Build);
            }

            return buffer.ToString();
        }
        public int CompareTo(ValueSemanticVersion2 other) => Compare(this, other);

        int IComparable.CompareTo(object obj) => Compare(this, (ValueSemanticVersion2)obj);

        private static void ParseInternal(string s, out string error, out ValueSemanticVersion2 value)
        {
            var match = SemanticVersionRegex.Match(s ?? string.Empty);
            if (!match.Success)
            {
                error = "String is not a valid semantic version.";
                value = default;
                return;
            }

            var major = BigInteger.Parse(match.Groups[1].Value);
            var minor = BigInteger.Parse(match.Groups[2].Value);
            var patch = BigInteger.Parse(match.Groups[3].Value);

            var prerelease = AH.NullIf(match.Groups[4].Value, string.Empty);
            var build = AH.NullIf(match.Groups[5].Value, string.Empty);

            error = null;
            value = new ValueSemanticVersion2(major, minor, patch, prerelease, build);
        }
    }
}
