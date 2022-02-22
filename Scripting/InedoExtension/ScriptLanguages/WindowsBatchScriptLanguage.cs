using System;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.Operations.Batch;

namespace Inedo.Extensions.Scripting.ScriptLanguages
{
    namespace WindowsBatch
    {

        public sealed class WindowsBatchScriptLanguage : ScriptLanguageInfo
        {
            public override string LanguageName => "Windows Batch";
            public override string FileExtension => ".bat";
            public override string SyntaxName => "batchfile";
            public override FileResource Icon { get; } = new EmbeddedFileResource("windows-batch.svg", "image/svg+xml");
            public override ScriptParameterUsage ParameterUsage => ScriptParameterUsage.EnvironmentVariable | ScriptParameterUsage.Arguments;
            public override Type CallOperationType => typeof(BatCallOperation);
            protected override ScriptInfo ParseScriptInfo(RaftItem2 script)
            {
                using var reader = script.OpenTextReader();
                return ScriptParser.Parse<WindowsBatchScriptParser>(reader);
            }
            protected override RichDescription GetCommandLineInstructions(ScriptInfo script, string argumentsFormat) => new($"> {script.FileName} {argumentsFormat}");
        }
    }
}
