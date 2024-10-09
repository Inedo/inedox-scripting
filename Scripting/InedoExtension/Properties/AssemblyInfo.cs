using System.Reflection;
using Inedo.Extensibility;

[assembly: AssemblyTitle("Scripting")]
[assembly: AssemblyDescription("Provides operations for PowerShell, Python, Batch, and shell scripting.")]
[assembly: AssemblyCompany("Inedo, LLC.")]
[assembly: AssemblyCopyright("Copyright © Inedo 2024")]
[assembly: AssemblyProduct("any")]

// Not for ProGet
[assembly: AppliesTo(InedoProduct.BuildMaster | InedoProduct.Otter)]

[assembly: ScriptNamespace("Scripting")]

[assembly: AssemblyVersion("2.4.0")]
[assembly: AssemblyFileVersion("2.4.0")]
