using Inedo.Diagnostics;

namespace Inedo.Extensions.Scripting.Operations.Python
{
    internal interface IPythonOperation : ILogSink
    {
        bool Verbose { get; }
        string PythonPath { get; }
        bool CaptureDebug { get; }
    }
}
