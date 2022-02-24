using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Inedo.ExecutionEngine.Variables;
using Inedo.Extensibility.ScriptLanguages;

namespace Inedo.Extensions.Scripting.ScriptLanguages.Batch
{
    internal sealed class WindowsBatchScriptParser : ScriptParser
    {
        private static readonly LazyRegex SectionTitleRegex = new(@"^(?<1>\w+)\s*:\s*(?<2>.+)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        protected override IEnumerable<string> ReadHeader(TextReader reader)
        {
            // skip any shebangs at the beginning or other comments

            string line;
            while (!string.IsNullOrWhiteSpace(line = reader.ReadLine()))
            {
                line = line.Trim();
                if (line.StartsWith("#") || line == string.Empty)
                    continue;

                break;
            }

            // look for the docstring

            if (line != null && (line.StartsWith("REM ") || line.StartsWith("REM")))
            {
                var sentinel = line.Substring(0, 3);
                var buffer = new StringBuilder();
                buffer.AppendLine(line.StartsWith("REM ") ? line.Substring(4) : line.Substring(3));
                while ((line = reader.ReadLine()) != null)
                {
                    int end = line.IndexOf(sentinel);
                    if (end < 0)
                    {
                        return Regex.Split(buffer.ToString(), @"\r?\n");
                    }
                    else
                    {
                        buffer.AppendLine(line.StartsWith("REM ") ? line.Substring(4) : line.Substring(3));
                    }
                }
            }

            return Enumerable.Empty<string>();
        }
        protected override (string content, string title) ParseLine(string line)
        {
            var m = SectionTitleRegex.Match(line);
            if (!startsWithWhiteSpace(line) && m.Success)
                return (m.Groups[2].Value, m.Groups[1].Value);
            else
                return (line, null);

            static bool startsWithWhiteSpace(string s) => string.IsNullOrWhiteSpace(s) || char.IsWhiteSpace(s[0]);
        }
        protected override ScriptInfo ParseSections(SectionList sections)
        {
            var summary = sections.GetMerged("AhDescription")?.Trim();
            if(string.IsNullOrWhiteSpace(summary))
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
            else if(parameters.Count > 0)
            {
                ahArgsFormatText = String.Join(" ", parameters.Select(p => $"${p.Name}"));
                try
                {
                    _ = ProcessedString.Parse(ahArgsFormatText);
                }
                catch (Exception ex)
                {
                    warnings.Add($"AhArgsFormat error: " + ex.Message);
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
