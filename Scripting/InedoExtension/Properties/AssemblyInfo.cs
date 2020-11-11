using System.Reflection;
using Inedo.Extensibility;

[assembly: AssemblyTitle("Scripting")]
[assembly: AssemblyDescription("Provides operations for powershell and shell scripting.")]
[assembly: AssemblyCompany("Inedo, LLC.")]
[assembly: AssemblyCopyright("Copyright © Inedo 2020")]
[assembly: AssemblyProduct("any")]

// Not for ProGet
[assembly: AppliesTo(InedoProduct.BuildMaster | InedoProduct.Otter)]

[assembly: ScriptNamespace("Scripting")]

[assembly: AssemblyVersion("1.9.0")]
[assembly: AssemblyFileVersion("1.9.0")]
