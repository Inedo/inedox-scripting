using System;
using System.IO;
using Inedo.Extensibility;

namespace Inedo.Extensions.Scripting.ScriptLanguages
{
    public sealed class EmbeddedFileResource : FileResource
    {
        private readonly string resourceName;
        private readonly Lazy<long> size;
        private readonly Lazy<DateTimeOffset> modified;

        public EmbeddedFileResource(string resourceName, string contentType)
        {
            this.resourceName = $"{typeof(EmbeddedFileResource).Namespace}.{resourceName}";
            this.size = new Lazy<long>(this.GetSize);
            this.modified = new Lazy<DateTimeOffset>(this.GetModified);
            this.ContentType = contentType;
        }

        public override string ContentType { get; }
        public override long Size => this.size.Value;
        public override DateTimeOffset? Modified => this.modified.Value;

        public override Stream OpenRead() => typeof(EmbeddedFileResource).Assembly.GetManifestResourceStream(this.resourceName);

        private long GetSize()
        {
            using var s = this.OpenRead();
            return s.Length;
        }
        private DateTimeOffset GetModified() => File.GetLastWriteTimeUtc(typeof(EmbeddedFileResource).Assembly.Location);
    }
}
