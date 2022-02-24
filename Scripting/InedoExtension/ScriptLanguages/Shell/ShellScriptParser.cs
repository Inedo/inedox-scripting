using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Inedo.ExecutionEngine.Variables;
using Inedo.Extensibility.ScriptLanguages;

namespace Inedo.Extensions.Scripting.ScriptLanguages.Shell
{
    internal sealed class ShellScriptParser : ScriptParser
    {
        private static readonly LazyRegex SectionTitleRegex = new(@"^(?<1>\w+)\s*:\s*(?<2>.+)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        protected override IEnumerable<string> ReadHeader(TextReader reader)
        {
            // skip any shebangs at the beginning or other comments

            string line;
            while (!string.IsNullOrWhiteSpace(line = reader.ReadLine()))
            {
                line = line.Trim();
                if (line.StartsWith("#!") || line == string.Empty)
                    continue;

                break;
            }

            // look for the docstring

            if (line != null && (line.StartsWith(": '") || line.StartsWith("#")))
            {
                var isMultiLineCommentHeader = line.StartsWith(": '");
                var buffer = new StringBuilder();
                while ((line = reader.ReadLine()) != null)
                {
                    
                    if ((isMultiLineCommentHeader && line.Trim().Equals("'")) || (!isMultiLineCommentHeader && !line.StartsWith("#")))
                    {
                        return Regex.Split(buffer.ToString(), @"\r?\n");
                    }
                    else if(isMultiLineCommentHeader)
                    {
                        buffer.Append(line);
                    }
                    else
                    {
                        buffer.AppendLine(line.StartsWith("# ") ? line.Substring(2) : line.Substring(1));
                    }
                }
            }

            return Enumerable.Empty<string>();
        }
        protected override (string content, string title) ParseLine(string line)
        {
            var m = SectionTitleRegex.Match(line);
            if (!startsWithWhiteSpace(line) && m.Success)
                return (null, m.Groups[1].Value);
            else
                return (line, null);

            static bool startsWithWhiteSpace(string s) => string.IsNullOrWhiteSpace(s) || char.IsWhiteSpace(s[0]);
        }
        protected override ScriptInfo ParseSections(SectionList sections)
        {
            var summary = sections.GetMerged("AhDescription")?.Trim();
            if (string.IsNullOrWhiteSpace(summary))
                summary = sections.GetMerged(string.Empty)?.Trim();
            var warnings = new List<string>();
            var parameters = new List<ScriptParameterInfo>();

            foreach (var paramText in GetParameters(sections, "AhParameters", "AhParameter"))
            {
                try
                {
                    parameters.Add(ScriptParameterInfo.Parse(paramText.Trim()));
                }
                catch (FormatException ex)
                {
                    warnings.Add($"AhParameter format error: " + ex.Message);
                }
            }

            var execMode = sections.GetMerged("AhExecMode")?.Trim();

            var configVars = sections.ReadConfigurationValues();

            var ahArgsFormatText = sections.GetMerged("AhArgsFormat")?.Trim();
            if (!string.IsNullOrEmpty(ahArgsFormatText))
            {
                try
                {
                    _ = ProcessedString.Parse(ahArgsFormatText);
                }
                catch (Exception ex)
                {
                    warnings.Add($"AhArgsFormat error: " + ex.Message);
                }
            }
            else
            {
                var arguments = parameters.Where(p => p.Usage == ScriptParameterUsage.Arguments);
                if(arguments.Count() > 0)
                {
                    ahArgsFormatText = String.Join(" ", arguments.Select(p => $"${p.Name}"));
                    try
                    {
                        _ = ProcessedString.Parse(ahArgsFormatText);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"AhArgsFormat error: " + ex.Message);
                    }
                }
            }

            return new ScriptInfo(parameters, summary, warnings, ahArgsFormatText, configVars, execMode);
        }

        private static IEnumerable<string> GetParameters(SectionList sections, params string[] titles)
        {
            return sections.GetMultiple(titles)
                .SelectMany(s => Regex.Split(s, @"\r?\n"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim());
        }
        
    }
}
