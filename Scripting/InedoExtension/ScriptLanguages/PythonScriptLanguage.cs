using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
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
        public override FileResource Icon => new EmbeddedFileResource("script-python", "image/svg+xml", 2118);
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
        private string ResourceName => resourceName.Value;

        private Lazy<Assembly> assembly;
        private Lazy<string> resourceName;

        public EmbeddedFileResource(string resourceName, string contentType, long size)
        {
            this.assembly = new Lazy<Assembly>(() => this.GetType().Assembly);
            this.resourceName = new Lazy<string>(() => assembly.Value.GetManifestResourceNames()
               .FirstOrDefault(n => n.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase))
            );
            this.ContentType = contentType;
            this.Size = size;
        }

        public override long Size { get; }

        public override string ContentType { get; }

        public override System.IO.Stream OpenRead() => assembly.Value.GetManifestResourceStream(this.ResourceName);
    }
}
