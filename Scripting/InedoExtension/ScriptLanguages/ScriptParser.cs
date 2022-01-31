using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Inedo.Extensibility.ScriptLanguages;

namespace Inedo.Extensions.Scripting.ScriptLanguages
{
    internal abstract class ScriptParser
    {
        protected ScriptParser()
        {
        }

        public static ScriptInfo Parse<TParser>(string script) where TParser : ScriptParser, new()
        {
            using var reader = new StringReader(script);
            return Parse<TParser>(reader);
        }
        public static ScriptInfo Parse<TParser>(TextReader reader) where TParser : ScriptParser, new()
        {
            var parser = new TParser();
            return parser.ParseInternal(reader);
        }

        protected abstract IEnumerable<string> ReadHeader(TextReader reader);
        protected abstract (string content, string title) ParseLine(string line);
        protected abstract ScriptInfo ParseSections(SectionList sections);

        private ScriptInfo ParseInternal(TextReader reader)
        {
            var sections = new SectionList();

            string currentTitle = null;
            var currentContent = new StringBuilder();

            foreach (var line in this.ReadHeader(reader))
            {
                var (content, title) = this.ParseLine(line);
                if (title == null)
                {
                    currentContent.AppendLine(content);
                }
                else
                {
                    sections.Add(currentTitle ?? string.Empty, currentContent.ToString());
                    currentTitle = title;
                    currentContent.Clear();
                    if (!string.IsNullOrEmpty(content))
                        currentContent.AppendLine(content);
                }
            }

            if (currentContent.Length > 0)
                sections.Add(currentTitle ?? string.Empty, currentContent.ToString());

            return this.ParseSections(sections);
        }

        protected sealed class SectionList
        {
            private readonly List<KeyValuePair<string, string>> sections = new();

            public IEnumerable<string> this[string title]
            {
                get
                {
                    foreach (var s in this.sections)
                    {
                        if (s.Key.Equals(title, StringComparison.OrdinalIgnoreCase))
                            yield return s.Value;
                    }
                }
            }

            public void Add(string title, string content)
            {
                this.sections.Add(new KeyValuePair<string, string>(title, content));
            }
            public string GetMerged(string title)
            {
                return string.Join(Environment.NewLine, this.sections.Where(s => s.Key.Equals(title, StringComparison.OrdinalIgnoreCase)).Select(s => s.Value));
            }
            public IEnumerable<string> GetTrimmed(string title)
            {
                foreach (var s in this[title])
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        yield return s.Trim();
                }
            }
            public IReadOnlyList<ScriptConfigurationValues> ReadConfigurationValues()
            {
                var results = new List<ScriptConfigurationValues>();

                var current = new ScriptConfigurationValues();
                var alreadyInclused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var section in sections)
                {
                    // if there's a duplicate key, treat this as the start of the next set of variables
                    if (!alreadyInclused.Add(section.Key))
                    {
                        results.Add(current);
                        current = new ScriptConfigurationValues();
                        alreadyInclused.Clear();
                        alreadyInclused.Add(section.Key);
                    }

                    if (section.Key.Equals("AhConfigType", StringComparison.OrdinalIgnoreCase))
                        current.ConfigType = section.Value.Trim();
                    else if (section.Key.Equals("AhConfigKey", StringComparison.OrdinalIgnoreCase))
                        current.ConfigKey = section.Value.Trim();
                    else if (section.Key.Equals("AhDesiredValue", StringComparison.OrdinalIgnoreCase))
                        current.DesiredValue = section.Value.Trim();
                    else if (section.Key.Equals("AhCurrentValue", StringComparison.OrdinalIgnoreCase))
                        current.CurrentValue = section.Value.Trim();
                    else if (section.Key.Equals("AhValueDrifted", StringComparison.OrdinalIgnoreCase))
                        current.ValueDrifted = section.Value.Trim();
                }

                if (current != null && (current.ConfigKey != null || current.ConfigType != null || current.CurrentValue != null || current.DesiredValue != null || current.ValueDrifted != null))
                    results.Add(current);

                return results.AsReadOnly();
            }
        }
    }
}
