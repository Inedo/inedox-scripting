namespace Inedo.Extensions.Scripting.PowerShell
{
    internal interface IPowerShellRunnerFactory
    {
        bool DebugLogging { get; }
        bool VerboseLogging { get; }
        bool PreferWindowsPowerShell { get; }
        bool TerminateHostProcess { get; }

        public PowerShellScriptRunner CreateRunner()
        {
            return new PowerShellScriptRunner
            {
                DebugLogging = this.DebugLogging,
                VerboseLogging = this.VerboseLogging,
                PreferWindowsPowerShell = this.PreferWindowsPowerShell,
                TerminateHostProcess = this.TerminateHostProcess
            };
        }
    }
}
