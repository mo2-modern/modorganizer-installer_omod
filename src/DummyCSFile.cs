using OMODFramework;
using OMODFramework.Scripting;
using RtfPipe;

namespace Dummy
{
    class Stub
    {
        static void Stub2()
        {
            // Discards, not locals: assigning to an unused local is CS0219. typeof
            // still anchors the assembly reference, which is this stub's only job.
            _ = typeof(OMOD);
            _ = typeof(IScriptFunctions);
            Rtf.ToHtml("");
        }
    }
}
