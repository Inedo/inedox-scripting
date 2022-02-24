using System;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensibility.ScriptLanguages;
using Inedo.Extensions.Scripting.Operations.Shell;


namespace Inedo.Extensions.Scripting.ScriptLanguages.Shell
{
    public sealed class ShellScriptingLanguage : ScriptLanguageInfo
    {
        public override string LanguageName => "Shell";
        public override string FileExtension => ".sh";
        public override string SyntaxName => "sh";
        public override FileResource Icon { get; } = new EmbeddedFileResource("Shell.bash.svg", "image/svg+xml");
        public override Type CallOperationType => typeof(SHCallOperation);
        public override ScriptParameterUsage ParameterUsage => ScriptParameterUsage.Arguments | ScriptParameterUsage.InputVariable | ScriptParameterUsage.OutputVariable;
        protected override ScriptInfo ParseScriptInfo(RaftItem2 script)
        {
            using var reader = script.OpenTextReader();
            return ScriptParser.Parse<ShellScriptParser>(reader);
        }

        protected override RichDescription GetCommandLineInstructions(ScriptInfo script, string argumentsFormat) => new($"> {script.FileName} {argumentsFormat}");
    }
}
