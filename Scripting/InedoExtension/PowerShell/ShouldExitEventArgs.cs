using System;

namespace Inedo.Extensions.Scripting.PowerShell
{
    internal sealed class ShouldExitEventArgs : EventArgs
    {
        public ShouldExitEventArgs(int exitCode)
        {
            this.ExitCode = exitCode;
        }

        public int ExitCode { get; }
    }
}
