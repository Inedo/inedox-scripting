using System.Reflection;
using Inedo.Extensibility;

[assembly: AssemblyTitle("Scripting")]
[assembly: AssemblyDescription("Provides operations for PowerShell and shell scripting.")]
[assembly: AssemblyCompany("Inedo, LLC.")]
[assembly: AssemblyCopyright("Copyright © Inedo 2021")]
[assembly: AssemblyProduct("any")]

// Not for ProGet
[assembly: AppliesTo(InedoProduct.BuildMaster | InedoProduct.Otter)]

[assembly: ScriptNamespace("Scripting")]

[assembly: AssemblyVersion("1.10.0")]
[assembly: AssemblyFileVersion("1.10.0")]
