using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using EnvDTE;

namespace TwinCatAssistant.Native
{
    public sealed class RunningIde
    {
        public string Registration { get; set; }
        public int ProcessId { get; set; }
        public DTE Dte { get; set; }

        public static IEnumerable<RunningIde> Enumerate()
        {
            IRunningObjectTable table;
            IBindCtx context;
            Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
            Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
            try
            {
                IEnumMoniker iterator;
                table.EnumRunning(out iterator);
                try
                {
                    var moniker = new IMoniker[1];
                    while (iterator.Next(1, moniker, IntPtr.Zero) == 0)
                    {
                        RunningIde ide = null;
                        try
                        {
                            string name;
                            moniker[0].GetDisplayName(context, null, out name);
                            if (name.StartsWith("!TcXaeShell.DTE.", StringComparison.OrdinalIgnoreCase)
                                || name.StartsWith("!VisualStudio.DTE.", StringComparison.OrdinalIgnoreCase))
                            {
                                object value;
                                table.GetObject(moniker[0], out value);
                                var dte = value as DTE;
                                uint pid;
                                if (dte != null && GetWindowThreadProcessId(new IntPtr(dte.MainWindow.HWnd), out pid) != 0)
                                    ide = new RunningIde { Registration = name, ProcessId = (int)pid, Dte = dte };
                            }
                        }
                        catch (COMException) { }
                        finally { if (moniker[0] != null) Marshal.ReleaseComObject(moniker[0]); }
                        if (ide != null) yield return ide;
                    }
                }
                finally { Marshal.ReleaseComObject(iterator); }
            }
            finally { Marshal.ReleaseComObject(context); Marshal.ReleaseComObject(table); }
        }

        [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
        [DllImport("ole32.dll")] private static extern int CreateBindCtx(int reserved, out IBindCtx context);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
