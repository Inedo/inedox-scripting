using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Inedo.Extensions.Scripting
{
    internal sealed class ExitCodeComparator
    {
        private static readonly string[] ValidOperators = new[] { "=", "==", "!=", "<", ">", "<=", ">=" };

        private ExitCodeComparator(string op, int value)
        {
            this.Operator = op;
            this.Value = value;
        }

        public string Operator { get; }
        public int Value { get; }

        public static ExitCodeComparator TryParse(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return null;

            var match = Regex.Match(s, @"^\s*(?<1>[=<>!])*\s*(?<2>[0-9]+)\s*$", RegexOptions.ExplicitCapture);
            if (!match.Success)
                return null;

            var op = match.Groups[1].Value;
            if (string.IsNullOrEmpty(op) || !ValidOperators.Contains(op))
                op = "==";

            return new ExitCodeComparator(op, int.Parse(match.Groups[2].Value));
        }

        public bool Evaluate(int exitCode)
        {
            return this.Operator switch
            {
                "=" or "==" => exitCode == this.Value,
                "!=" => exitCode != this.Value,
                "<" => exitCode < this.Value,
                ">" => exitCode > this.Value,
                "<=" => exitCode <= this.Value,
                ">=" => exitCode >= this.Value,
                _ => false
            };
        }
    }
}
