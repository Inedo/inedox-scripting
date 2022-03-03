using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Inedo.ExecutionEngine.Variables;
using Inedo.Extensibility.ScriptLanguages;

namespace Inedo.Extensions.Scripting.ScriptLanguages.Python
{
    internal sealed class PythonScriptParser : ScriptParser
    {
        private static readonly LazyRegex EscapeRegex = new(@"\\(?<1>[\\'""abfnrt]|N\{[^\}]*}|u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|[0-7]{3}|x[0-9a-fA-F]{2})", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        private static readonly LazyRegex SectionTitleRegex = new(@"^(?<1>\w+)\s*:\s*$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

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

            if (line != null && (line.StartsWith("\"\"\"") || line.StartsWith("'''")))
            {
                var sentinel = line.Substring(0, 3);
                var buffer = new StringBuilder();
                ProcessEscapeCharacters(line.Substring(3), buffer);
                while ((line = reader.ReadLine()) != null)
                {
                    int end = line.IndexOf(sentinel);
                    if (end >= 0)
                    {
                        ProcessEscapeCharacters(line.Substring(0, end), buffer);
                        return Regex.Split(buffer.ToString(), @"\r?\n");
                    }
                    else
                    {
                        ProcessEscapeCharacters(line, buffer);
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
            var summary = sections.GetMerged(string.Empty)?.Trim();
            var warnings = new List<string>();
            var parameters = new List<ScriptParameterInfo>();

            foreach (var paramText in GetParameters(sections, "AhParameters"))
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

            return new ScriptInfo(parameters, summary, warnings, ahArgsFormatText, configVars, execMode);
        }

        private static IEnumerable<string> GetParameters(SectionList sections, string title)
        {
            return sections[title]
                .SelectMany(s => Regex.Split(s, @"\r?\n"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim());
        }
        private static void ProcessEscapeCharacters(string line, StringBuilder buffer)
        {
            var replaced = EscapeRegex.Replace(
                line,
                m =>
                {
                    var v = m.Groups[1].Value;
                    if (v.Length == 1)
                    {
                        return v switch
                        {
                            "a" => "\a",
                            "b" => "\b",
                            "f" => "\f",
                            "n" => "\n",
                            "r" => "\r",
                            "t" => "\t",
                            _ => v
                        };
                    }

                    if (v.StartsWith("N"))
                        return v.Substring(1); //lookup by character name is not inclused

                    if (v.StartsWith("u"))
                        return BitConverter.ToChar(AH.ParseHex(v.Substring(1)), 0).ToString();

                    if (v.StartsWith("U"))
                        return char.ConvertFromUtf32(BitConverter.ToInt32(AH.ParseHex(v.Substring(1)), 0));

                    if (v.StartsWith("x"))
                        return ((char)AH.ParseHex(v.Substring(1))[0]).ToString();

                    // remaining case is octal notation
                    int d1 = v[1] - '0';
                    int d2 = v[2] - '0';
                    int d3 = v[3] - '0';
                    return ((char)(d1 * 64 + d2 * 8 + d3)).ToString();
                }
            );

            buffer.AppendLine(replaced);
        }
    }
}
