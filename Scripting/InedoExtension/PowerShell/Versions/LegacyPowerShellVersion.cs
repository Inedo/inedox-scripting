using System;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Inedo.Extensions.Scripting.PowerShell.Versions
{
    /// <summary>
    /// Represents a NuGet package version.
    /// </summary>
    [Serializable]
    public sealed class LegacyPowerShellVersion : PowerShellVersion, IComparable<LegacyPowerShellVersion>, IEquatable<LegacyPowerShellVersion>
    {
        private static readonly LazyRegex SemanticVersionRegex = new LazyRegex(@"^(?<1>[0-9]+)(\.(?<2>[0-9]+)){0,3}(-(?<3>[a-zA-Z][0-9a-zA-Z-]*))?(\+[a-zA-Z0-9\-\.]*)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Singleline);
        private static readonly LazyRegex ValidatePrereleaseRegex = new LazyRegex(@"^[a-zA-Z][0-9a-zA-Z-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private readonly int innerMinor = -1;
        private readonly int innerPatch = -1;
        private readonly int innerBuild = -1;
        private readonly Lazy<Version> getLegacyVersion;

        /// <summary>
        /// Initializes a new instance of the <see cref="PowerShellVersion" /> class.
        /// </summary>
        /// <param name="version">The version.</param>
        public LegacyPowerShellVersion(string version)
            : base(version)
        {
            var value = TryParse(version) ?? throw new FormatException();
            this.MyMajor = value.MyMajor;
            this.innerMinor = value.innerMinor;
            this.innerPatch = value.innerPatch;
            this.innerBuild = value.innerBuild;
            this.SpecialVersion = value.SpecialVersion;
            this.getLegacyVersion = value.getLegacyVersion;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PowerShellVersion" /> class.
        /// </summary>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        /// <param name="patch">The patch number.</param>
        /// <param name="build">The build numer.</param>
        public LegacyPowerShellVersion(int major, int minor, int patch, int build)
            : this(major, minor, patch, build, null, null)
        {
            if (minor < 0)
                throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0)
                throw new ArgumentOutOfRangeException(nameof(patch));
            if (build < 0)
                throw new ArgumentNullException(nameof(build));
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PowerShellVersion" /> class.
        /// </summary>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        /// <param name="patch">The patch number.</param>
        /// <param name="specialVersion">The prerelease version.</param>
        public LegacyPowerShellVersion(int major, int minor, int patch, string specialVersion)
            : this(major, minor, patch, -1, ValidatePrereleaseString(specialVersion), null)
        {
            if (minor < 0)
                throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0)
                throw new ArgumentOutOfRangeException(nameof(patch));
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PowerShellVersion" /> class.
        /// </summary>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        /// <param name="specialVersion">The prerelease version.</param>
        public LegacyPowerShellVersion(int major, int minor, string specialVersion)
            : this(major, minor, -1, -1, ValidatePrereleaseString(specialVersion), null)
        {
            if (minor < 0)
                throw new ArgumentOutOfRangeException(nameof(minor));
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PowerShellVersion" /> class.
        /// </summary>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        public LegacyPowerShellVersion(int major, int minor)
            : this(major, minor, null)
        {
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PowerShellVersion" /> class.
        /// </summary>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        /// <param name="patch">The patch number.</param>
        public LegacyPowerShellVersion(int major, int minor, int patch)
            : this(major, minor, patch, null)
        {
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PowerShellVersion" /> class.
        /// </summary>
        /// <param name="version">The version.</param>
        public LegacyPowerShellVersion(Version version)
            : this(version, null)
        {
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PowerShellVersion" /> class.
        /// </summary>
        /// <param name="version">The version.</param>
        /// <param name="specialVersion">The special version.</param>
        public LegacyPowerShellVersion(Version version, string specialVersion)
            : base(null)
        {
            if (version == null)
                throw new ArgumentNullException(nameof(version));

            this.MyMajor = version.Major;
            if (version.Minor >= 0)
            {
                this.innerMinor = version.Minor;
                if (version.Build >= 0)
                {
                    this.innerPatch = version.Build;
                    if (version.Revision >= 0)
                        this.innerBuild = version.Revision;
                }
            }

            this.SpecialVersion = ValidatePrereleaseString(specialVersion);
            this.getLegacyVersion = new Lazy<Version>(this.BuildLegacyVersion, LazyThreadSafetyMode.PublicationOnly);
        }

        private LegacyPowerShellVersion(int major, int minor, int patch, int build, string prerelease, string originalVersion)
            : base(originalVersion)
        {
            if (major < 0)
                throw new ArgumentOutOfRangeException(nameof(major));

            this.MyMajor = major;
            this.innerMinor = minor;
            this.innerPatch = patch;
            this.innerBuild = build;
            this.SpecialVersion = prerelease;
            this.getLegacyVersion = new Lazy<Version>(this.BuildLegacyVersion, LazyThreadSafetyMode.PublicationOnly);
        }

        public override BigInteger Major => this.MyMajor;
        public override BigInteger? Minor => this.MyMinor;
        public override BigInteger? Patch => this.MyPatch;
        public override string Prerelease => this.SpecialVersion;
        public override string UniquePart => this.ToString();
        public override string OriginalUniquePart
        {
            get
            {
                var index = this.OriginalString.IndexOf("+");
                if (index != -1)
                    return this.OriginalString.Substring(0, index);

                return this.OriginalString;
            }
        }

        /// <summary>
        /// Gets the major version number. This is the first part of the version.
        /// </summary>
        public int MyMajor { get; }
        /// <summary>
        /// Gets the minor version number. This is the second part of the version.
        /// </summary>
        public int? MyMinor => this.innerMinor >= 0 ? (int?)this.innerMinor : null;
        /// <summary>
        /// Gets the patch number. This is the third part of the version.
        /// </summary>
        public int? MyPatch => this.innerPatch >= 0 ? (int?)this.innerPatch : null;
        /// <summary>
        /// Gets the build number. This is the fourth part of the version.
        /// </summary>
        public int? MyBuild => this.innerBuild >= 0 ? (int?)this.innerBuild : null;
        /// <summary>
        /// Gets the prerelease text.
        /// </summary>
        public string SpecialVersion { get; }

        public static LegacyPowerShellVersion TryParse(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var match = SemanticVersionRegex.Value.Match(version);
            if (!match.Success)
                return null;

            int major = int.Parse(match.Groups[1].Value);
            var captures = match.Groups[2].Captures;

            int minor = -1;
            if (captures.Count >= 1)
                minor = int.Parse(captures[0].Value);

            int patch = -1;
            if (captures.Count >= 2)
                patch = int.Parse(captures[1].Value);

            int build = -1;
            if (captures.Count >= 3)
                build = int.Parse(captures[2].Value);

            var prerelease = match.Groups[3].Value;

            return new LegacyPowerShellVersion(major, minor, patch, build, prerelease, version);
        }
        public override PowerShellVersion ParseSame(string version) => TryParse(version) ?? throw new FormatException();
        public override PowerShellVersion TryParseSame(string version) => TryParse(version);

        public static bool Equals(LegacyPowerShellVersion a, LegacyPowerShellVersion b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (ReferenceEquals(a, null) | ReferenceEquals(b, null))
                return false;

            return a.MyMajor == b.MyMajor
                && a.innerMinor == b.innerMinor
                && a.innerPatch == b.innerPatch
                && (a.innerBuild == b.innerBuild || (a.innerBuild <= 0 && b.innerBuild <= 0))
                && string.Equals(a.SpecialVersion, b.SpecialVersion, StringComparison.OrdinalIgnoreCase);
        }
        public static int Compare(LegacyPowerShellVersion a, LegacyPowerShellVersion b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (ReferenceEquals(a, null))
                return -1;
            if (ReferenceEquals(b, null))
                return 1;

            if (a.MyMajor == b.MyMajor)
            {
                if (a.innerMinor == b.innerMinor)
                {
                    if (a.innerPatch == b.innerPatch)
                    {
                        if (a.innerBuild == b.innerBuild || (a.innerBuild <= 0 && b.innerBuild <= 0))
                        {
                            if (!string.IsNullOrEmpty(a.SpecialVersion) && !string.IsNullOrEmpty(b.SpecialVersion))
                                return string.Compare(a.SpecialVersion, b.SpecialVersion, StringComparison.OrdinalIgnoreCase);
                            else if (string.IsNullOrEmpty(a.SpecialVersion) && !string.IsNullOrEmpty(b.SpecialVersion))
                                return 1;
                            else if (!string.IsNullOrEmpty(a.SpecialVersion) && string.IsNullOrEmpty(b.SpecialVersion))
                                return -1;
                            else
                                return 0;
                        }
                        else
                        {
                            return a.innerBuild.CompareTo(b.innerBuild);
                        }
                    }
                    else
                    {
                        return a.innerPatch.CompareTo(b.innerPatch);
                    }
                }
                else
                {
                    return a.innerMinor.CompareTo(b.innerMinor);
                }
            }
            else
            {
                return a.MyMajor.CompareTo(b.MyMajor);
            }
        }

        public int CompareTo(LegacyPowerShellVersion other) => Compare(this, other);
        public override int CompareTo(PowerShellVersion other)
        {
            if (other is LegacyPowerShellVersion v)
                return this.CompareTo(v);
            if (other is InvalidPowerShellVersion)
                return -1;
            return LegacyAndSemVerCompare(this, (SemVer2PowerShellVersion)other);
        }

        public override string ToNormalizedString() => this.ToString(true);
        public override string ToString() => this.ToString(false);
        public bool Equals(LegacyPowerShellVersion other) => Equals(this, other);
        public override bool Equals(PowerShellVersion other)
        {
            if (other is LegacyPowerShellVersion l)
                return this.Equals(l);
            else if (other is SemVer2PowerShellVersion v)
                return LegacyAndSemVerEquals(this, v);
            else
                return false;
        }

        public override int GetHashCode()
        {
            int num = 0;
            num |= (this.MyMajor & 15) << 0x1c;
            num |= (this.innerMinor & 0xff) << 20;
            num |= (this.innerPatch & 0xff) << 12;
            return (num | (this.innerBuild & 0xfff));
        }
        public override string GetAlias()
        {
            if (this.innerBuild < 0)
                return new LegacyPowerShellVersion(this.MyMajor, this.innerMinor, this.innerPatch, 0, this.Prerelease, this.OriginalString).ToString();
            else if (this.innerBuild == 0)
                return this.ToNormalizedString();
            else
                return null;
        }
        public override PowerShellVersion ToNormalizedVersion()
        {
            if (this.innerBuild == 0)
                return new LegacyPowerShellVersion(this.MyMajor, this.innerMinor, this.innerPatch, -1, this.Prerelease, this.OriginalString);
            else
                return this;
        }

        private string ToString(bool normalized)
        {
            var buffer = new StringBuilder(50);
            buffer.Append(this.MyMajor);
            if (this.innerMinor >= 0)
            {
                buffer.Append('.');
                buffer.Append(this.innerMinor);

                if (this.innerPatch >= 0)
                {
                    buffer.Append('.');
                    buffer.Append(this.innerPatch);

                    if (this.innerBuild >= 0)
                    {
                        if (!normalized || this.innerBuild > 0)
                        {
                            buffer.Append('.');
                            buffer.Append(this.innerBuild);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(this.SpecialVersion))
            {
                buffer.Append('-');
                buffer.Append(this.SpecialVersion);
            }

            return buffer.ToString();
        }

        private static string ValidatePrereleaseString(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (!ValidatePrereleaseRegex.Value.IsMatch(s))
                throw new ArgumentException("The prerelease version is invalid.");

            return s;
        }

        private Version BuildLegacyVersion()
        {
            int major = this.MyMajor;
            int minor = this.MyMinor ?? 0;

            if (this.innerPatch >= 0)
            {
                int patch = this.innerPatch;

                if (this.innerBuild >= 0)
                {
                    int build = this.innerBuild;
                    return new Version(major, minor, patch, build);
                }
                else
                {
                    return new Version(major, minor, patch);
                }
            }
            else
            {
                return new Version(major, minor);
            }
        }
    }
}
