using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inedo.Diagnostics;

namespace Inedo.Extensions.Scripting.Operations.Shell
{
    internal interface IShellOperation : ILogSink
    {
        bool Verbose { get; }
        bool CaptureDebug { get; }
    }
}
