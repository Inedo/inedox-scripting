using Inedo.Diagnostics;

namespace Inedo.Extensions.Scripting
{
    internal sealed class NullLogSink : ILogSink
    {
        public static NullLogSink Instance { get; } = new NullLogSink();

        public void Log(IMessage message)
        {
            // discard
        }
    }
}
