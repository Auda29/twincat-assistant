using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
internal static class KnowledgeLauncher {
    private static string Quote(string s) {
        var b = new StringBuilder("\""); int slashes=0;
        foreach (char c in s) { if(c=='\\') { slashes++; continue; } if(c=='\"') { b.Append('\\',slashes*2+1); b.Append(c); slashes=0; continue; } b.Append('\\',slashes); slashes=0; b.Append(c); }
        b.Append('\\',slashes*2); b.Append('"'); return b.ToString();
    }
    private static int Main(string[] args) {
        try {
            var python=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"python","python.exe");
            var command=new StringBuilder("-I -B -m twincat_knowledge");
            foreach(var arg in args) command.Append(' ').Append(Quote(arg));
            var info=new ProcessStartInfo(python,command.ToString()) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Environment.CurrentDirectory,
                RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(info)) {
                var input = new Thread(() => { try { Pump(Console.OpenStandardInput(), process.StandardInput.BaseStream); process.StandardInput.Close(); } catch {} });
                var output = new Thread(() => Pump(process.StandardOutput.BaseStream, Console.OpenStandardOutput()));
                var error = new Thread(() => Pump(process.StandardError.BaseStream, Console.OpenStandardError()));
                input.IsBackground=true; output.IsBackground=true; error.IsBackground=true;
                input.Start(); output.Start(); error.Start();
                process.WaitForExit(); output.Join(); error.Join(); return process.ExitCode;
            }
        } catch { Console.Error.WriteLine("Knowledge runtime failed to start."); return 1; }
    }
    private static void Pump(Stream source, Stream destination) {
        var buffer=new byte[8192]; int count;
        while((count=source.Read(buffer,0,buffer.Length))>0) { destination.Write(buffer,0,count); destination.Flush(); }
    }
}
