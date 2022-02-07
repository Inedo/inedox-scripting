using System;
using System.ComponentModel;
using System.IO;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.Operations.Python;

namespace Inedo.Extensions.Scripting.ScriptLanguages
{
    [DisplayName("Python")]
    [Description("Parses additional headers in Python scripts.")]
    public sealed class PythonScriptLanguage : ScriptLanguageInfo
    {
        public override string LanguageName => "Python";
        public override string SyntaxName => "python";
        public override string FileExtension => ".py";
        public override FileResource Icon => new EmbeddedFileResource("script-python.svg", "image/svg+xml");
        public override ScriptParameterUsage ParameterUsage => ScriptParameterUsage.InputVariable | ScriptParameterUsage.OutputVariable | ScriptParameterUsage.EnvironmentVariable | ScriptParameterUsage.Arguments;
        public override Type CallOperationType => typeof(PYCallOperation);
        public override Type EnsureOperationType => typeof(PYEnsureOperation);
        public override Type VerifyOperationType => typeof(PYVerifyOperation);

        protected override RichDescription GetCommandLineInstructions(ScriptInfo info, string arguments) => new($"python {info.FileName} {arguments}");
        protected override ScriptInfo ParseScriptInfo(RaftItem2 script)
        {
            using var reader = script.OpenTextReader();
            return ScriptParser.Parse<PythonScriptParser>(reader);
        }
    }

    public sealed class EmbeddedFileResource : FileResource
    {
        private readonly string resourceName;
        private readonly Lazy<long> size;
        private readonly Lazy<DateTimeOffset> modified;

        public EmbeddedFileResource(string resourceName, string contentType)
        {
            this.resourceName = $"{typeof(PythonScriptLanguage).Namespace}.{resourceName}";
            this.size = new Lazy<long>(this.GetSize);
            this.modified = new Lazy<DateTimeOffset>(this.GetModified);
            this.ContentType = contentType;
        }

        public override string ContentType { get; }
        public override long Size => this.size.Value;
        public override DateTimeOffset? Modified => this.modified.Value;

        public override Stream OpenRead() => typeof(PythonScriptLanguage).Assembly.GetManifestResourceStream(this.resourceName);

        private long GetSize()
        {
            using var s = this.OpenRead();
            return s.Length;
        }
        private DateTimeOffset GetModified() => File.GetLastWriteTimeUtc(typeof(PythonScriptLanguage).Assembly.Location);
    }
}
