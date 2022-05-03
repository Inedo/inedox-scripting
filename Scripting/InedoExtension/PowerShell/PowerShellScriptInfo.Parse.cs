using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.PowerShell
{
    partial class PowerShellScriptInfo
    {
        private static readonly LazyRegex DocumentationRegex = new LazyRegex(@"\s*\.(?<1>\S+)[ \t]*(?<2>[^\r\n]+)?\s*\n(?<3>(.(?!\n\.))+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
        private static readonly LazyRegex SpaceCollapseRegex = new LazyRegex(@"\s*\n\s*", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly LazyRegex ParameterTypeRegex = new LazyRegex(@"^\[?(?<1>[^\]]+)\]?$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        public static bool TryParse(TextReader scriptText, out PowerShellScriptInfo info)
        {
            try
            {
                info = Parse(scriptText);
                return true;
            }
            catch
            {
                info = null;
                return false;
            }
        }
        public static PowerShellScriptInfo Parse(TextReader scriptText)
        {
            if (scriptText == null)
                throw new ArgumentNullException(nameof(scriptText));

            var tokens = PSParser.Tokenize(scriptText.ReadToEnd(), out var errors);

            int paramIndex = tokens
                .TakeWhile(t => t.Type != PSTokenType.Keyword || !string.Equals(t.Content, "param", StringComparison.OrdinalIgnoreCase))
                .Count();

            var parameters = ScrapeParameters(tokens.Skip(paramIndex + 1)).ToList();

            var documentationToken = tokens
                .Take(paramIndex)
                .Where(t => t.Type == PSTokenType.Comment && t.Content != null && t.Content.StartsWith("<#") && t.Content.EndsWith("#>"))
                .LastOrDefault();

            if (documentationToken != null)
            {
                var documentation = documentationToken.Content;
                if (documentation.StartsWith("<#") && documentation.EndsWith("#>"))
                    documentation = documentation.Substring(2, documentation.Length - 4);

                var docBlocks = DocumentationRegex
                    .Value
                    .Matches(documentation)
                    .Cast<Match>()
                    .Select(m => new
                    {
                        Name = m.Groups[1].Value,
                        Param = !string.IsNullOrWhiteSpace(m.Groups[2].Value) ? m.Groups[2].Value.Trim() : null,
                        Content = !string.IsNullOrWhiteSpace(m.Groups[3].Value) ? SpaceCollapseRegex.Value.Replace(m.Groups[3].Value.Trim(), " ") : null
                    })
                    .Where(d => d.Content != null)
                    .ToLookup(
                        d => d.Name,
                        d => ( d.Param, d.Content),
                        StringComparer.OrdinalIgnoreCase);

                return new PowerShellScriptInfo
                {
                    ExecutionModeVariableName = docBlocks["AHEXECMODE"].FirstOrDefault().Content,
                    ConfigParameters = Array.AsReadOnly(PSConfigParameterInfo.FromDocumentationBlocks(docBlocks).ToArray()),
                    Description = docBlocks["SYNOPSIS"].Concat(docBlocks["DESCRIPTION"]).Select(d => d.Content).FirstOrDefault(),
                    Parameters = Array.AsReadOnly(parameters.GroupJoin(
                        docBlocks["PARAMETER"],
                        p => p.Name,
                        d => d.Param,
                        (p, d) => new PowerShellParameterInfo(
                            name: p.Name,
                            description: d.Select(t => t.Content).FirstOrDefault(),
                            defaultValue: p.DefaultValue,
                            isBooleanOrSwitch: p.IsBooleanOrSwitch,
                            isOutput: p.IsOutput,
                            mandatory: p.Mandatory
                        ),
                        StringComparer.OrdinalIgnoreCase).ToArray())
                };
            }

            return new PowerShellScriptInfo
            {
                Parameters = Array.AsReadOnly(parameters.Select(p => new PowerShellParameterInfo(
                    name: p.Name,
                    defaultValue: p.DefaultValue,
                    isBooleanOrSwitch: p.IsBooleanOrSwitch,
                    isOutput: p.IsOutput
                )).ToArray())
            };
        }

        public static PowerShellScriptInfo TryLoad(LooselyQualifiedName scriptName, object context = null)
        {
            // this is a preposterous hack, and should be removed as soon as we get context added to SDK (see SDK-74)
            if (SDK.ProductName == "BuildMaster")
            {
                // this is only ever called from the planeditor page, but there are multiple ways to get the data...
                if (HttpContextThatWorksOnLinux.Current == null)
                    return null;

                // the really easy way to get the applicationId (building the operation editor)
                var applicationId = AH.ParseInt(HttpContextThatWorksOnLinux.Current.Request.QueryString["applicationId"]);
                if (!applicationId.HasValue)
                {
                    // the "other" ways (rebuilding TSSatements)
                    var fullItemId = AH.CoalesceString(
                        HttpContextThatWorksOnLinux.Current.Request.QueryString["planId"],
                        HttpContextThatWorksOnLinux.Current.Request.Form["additionalContext"]
                    );
                    if (string.IsNullOrEmpty(fullItemId))
                        return null;

                    // this is logic based on RaftItemId.TryParse
                    var parts = fullItemId.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 4)
                        return null;

                    if (string.Equals(parts[1], "global", StringComparison.OrdinalIgnoreCase))
                        return null;

                    // find the appId by name
                    var appIdtyp = Type.GetType("Inedo.BuildMaster.ApplicationId,BuildMaster");
                    var appId = Activator.CreateInstance(appIdtyp, parts[1]);
                    applicationId = (int)appIdtyp.GetProperty("Id").GetValue(appId);
                }

                // stuff into a SimpleContext
                var ctxTyp = Type.GetType("Inedo.BuildMaster.Extensibility.SimpleBuildMasterContext,BuildMaster");
                context = Activator.CreateInstance(ctxTyp, /*applicationGroupId*/null, applicationId, /*deployableId*/null, /*executionId*/null, /*environmentId*/null, /*serverId*/null, /*promotionId*/null, /*serverRoleId*/null, /*releaseId*/null, /*buildId*/null);
            }
            // END HACK

            var name = scriptName.FullName;
            if (!name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                name += ".ps1";

            var item = SDK.GetRaftItem(RaftItemType.Script, name, context);
            if (item == null)
                return null;

            using var reader = new StringReader(item.Content);
            _ = TryParse(reader, out var info);
            return info;
        }

        private static IEnumerable<ParamInfo> ScrapeParameters(IEnumerable<PSToken> tokens)
        {
            int groupDepth = 0;
            var paramTokens = new List<PSToken>();

            var filteredTokens = tokens
                .Where(t => t.Type != PSTokenType.Comment && t.Type != PSTokenType.NewLine);

            foreach (var token in filteredTokens)
            {
                paramTokens.Add(token);

                if (token.Type == PSTokenType.GroupStart && token.Content == "(")
                    groupDepth++;

                if (token.Type == PSTokenType.GroupEnd && token.Content == ")")
                {
                    groupDepth--;
                    if (groupDepth <= 0)
                        break;
                }
            }

            var currentParam = new ParamInfo();

            bool expectDefaultValue = false;

            for (int i = 0; i < paramTokens.Count; i++)
            {
                var token = paramTokens[i];

                if (token.Type == PSTokenType.Operator && token.Content != "=")
                {
                    expectDefaultValue = false;
                    if (currentParam.Name != null)
                        yield return currentParam;

                    currentParam = new ParamInfo();
                    continue;
                }

                if (expectDefaultValue)
                {
                    currentParam.DefaultValue = token.Content;
                    expectDefaultValue = false;
                    continue;
                }

                if (token.Type == PSTokenType.Attribute && token.Content.Equals("Parameter", StringComparison.OrdinalIgnoreCase))
                {
                    var groupCount = 0;
                    for (int j = i + 1; j < paramTokens.Count; j++)
                    {
                        if (paramTokens[j].Type == PSTokenType.GroupStart)
                            groupCount++;

                        else if (paramTokens[j].Type == PSTokenType.GroupEnd)
                            groupCount--;

                        if (groupCount <= 0)
                            break;

                        if (paramTokens[j].Type == PSTokenType.Member && paramTokens[j].Content.Equals("Mandatory", StringComparison.OrdinalIgnoreCase))
                            continue;

                        currentParam.Mandatory = true;

                        // just in case someone does Mandatory=$false
                        if (!(paramTokens.Count < j + 2)
                            && paramTokens[j + 1].Type == PSTokenType.Operator && paramTokens[j + 1].Content == "="
                            && paramTokens[j + 2].Type == PSTokenType.Variable && !paramTokens[j + 2].Content.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            currentParam.Mandatory = false;
                        }
                    }

                    continue;
                }
                if (token.Type == PSTokenType.Type)
                {
                    var match = ParameterTypeRegex.Value.Match(token.Content ?? string.Empty);
                    if (match.Success)
                        currentParam.Type = match.Groups[1].Value;
                }

                if (token.Type == PSTokenType.Variable)
                    currentParam.Name = token.Content;

                if (token.Type == PSTokenType.Operator && token.Content == "=")
                {
                    expectDefaultValue = true;
                    continue;
                }
            }

            if (currentParam.Name != null)
                yield return currentParam;
        }
        private sealed class ParamInfo
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string DefaultValue { get; set; }
            public bool Mandatory { get; set; }
            public bool IsBooleanOrSwitch
            {
                get
                {
                    return string.Equals(this.Type, "switch", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(this.Type, "bool", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(this.Type, "System.Boolean", StringComparison.OrdinalIgnoreCase);
                }
            }
            public bool IsOutput
            {
                get
                {
                    return string.Equals(this.Type, "ref", StringComparison.OrdinalIgnoreCase);
                }
            }

            public override string ToString()
            {
                if (this.Type != null)
                    return "[" + this.Type + "] " + this.Name;
                else
                    return this.Name;
            }
        }
    }
}
