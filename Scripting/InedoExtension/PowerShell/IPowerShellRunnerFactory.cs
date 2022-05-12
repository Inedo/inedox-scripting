namespace Inedo.Extensions.Scripting.PowerShell
{
    internal interface IPowerShellRunnerFactory
    {
        bool DebugLogging { get; }
        bool VerboseLogging { get; }
        bool PreferWindowsPowerShell { get; }

        public PowerShellScriptRunner CreateRunner()
        {
            return new PowerShellScriptRunner
            {
                DebugLogging = this.DebugLogging,
                VerboseLogging = this.VerboseLogging,
                PreferWindowsPowerShell = this.PreferWindowsPowerShell
            };
        }
    }
}
