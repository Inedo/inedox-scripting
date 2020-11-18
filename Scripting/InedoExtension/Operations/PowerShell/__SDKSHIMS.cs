namespace Inedo.Web.Editors.Operations
{
    using System;
    using System.Collections.Generic;

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class UsesCallScriptEditorAttribute : Attribute
    {
        public UsesCallScriptEditorAttribute(Type callScriptInfoProvider)
        {
            this.CallScriptInfoProvider = callScriptInfoProvider;
        }

        public Type CallScriptInfoProvider { get; }
    }
    public interface ICallScriptInfoProvider
    {
        CallScriptInfo TryLoad(string name);
    }
    public sealed class CallScriptInfo
    {
        public CallScriptInfo(string name, IEnumerable<CallScriptArgument> parameters)
        {
            this.Name = name;
            this.Parameters = parameters;
        }
        public string Name { get; }
        public IEnumerable<CallScriptArgument> Parameters { get; }
    }
    public sealed class CallScriptArgument
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string DefaultValue { get; set; }
        public string Value { get; set; }
        public bool IsBooleanOrSwitch { get; set; }
        public bool IsOutput { get; set; }
    }
}