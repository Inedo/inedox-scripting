﻿using System.Reflection;
using Inedo.Extensibility;

[assembly: AssemblyTitle("Scripting")]
[assembly: AssemblyDescription("Provides operations for PowerShell, Python, and shell scripting.")]
[assembly: AssemblyCompany("Inedo, LLC.")]
[assembly: AssemblyCopyright("Copyright © Inedo 2022")]
[assembly: AssemblyProduct("any")]

// Not for ProGet
[assembly: AppliesTo(InedoProduct.BuildMaster | InedoProduct.Otter)]

[assembly: ScriptNamespace("Scripting")]

[assembly: AssemblyVersion("1.14.0")]
[assembly: AssemblyFileVersion("1.14.0")]
