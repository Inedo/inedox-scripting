using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Inedo.Extensibility;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Web;

namespace Inedo.Extensions.Scripting.SuggestionProviders;

internal sealed class ScriptNameSuggestionProvider : ISuggestionProvider
{
    public IAsyncEnumerable<string> GetSuggestionsAsync(IComponentConfiguration config, CancellationToken cancellationToken)
    {
        return getItems().ToAsyncEnumerable();

        IEnumerable<string> getItems()
        {
            return SDK.GetRaftItems(RaftItemType.Script, config.EditorContext)
                .Where(i => i.Name.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Name)
                .Select(i => i.Name);
        }
    }
}
