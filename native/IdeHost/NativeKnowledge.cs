using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TwinCatAssistant.Native
{
    internal sealed class KnowledgeSource
    {
        public string path { get; set; }
        public string language { get; set; }
        public string catalog { get; set; }
        public string name { get; set; }
        public string DisplayLabel { get {
            var match=System.Text.RegularExpressions.Regex.Match(name??"",@"\.([0-9]+)\.mshc$");
            return (language == "de" ? "Deutsch" : "Englisch") + " · " + (match.Success?"Stand "+match.Groups[1].Value:name) + " · " + catalog;
        } }
    }
    internal sealed class KnowledgeState
    {
        public string phase { get; set; }
        public bool ready { get; set; }
        public bool complete { get; set; }
        public KnowledgeSource[] candidates { get; set; }
        public string language { get; set; }
        public int topics { get; set; }
        public string revision { get; set; }
    }
    internal static class NativeKnowledge
    {
        internal static async Task<KnowledgeState> RunAsync(NativeChatConfiguration config, object request,
            Action<KnowledgeState> progress, CancellationToken cancel)
        {
            config.Validate();
            var start = new ProcessStartInfo(config.NodePath, NativeChatSession.Quote(Path.Combine(config.RepositoryRoot,"src","native-knowledge.mjs"))) {
                WorkingDirectory=config.RepositoryRoot, UseShellExecute=false, CreateNoWindow=true,
                RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true
            };
            start.EnvironmentVariables.Clear();
            foreach (var key in new[] { "PATH","Path","SystemRoot","WINDIR","COMSPEC","PATHEXT","TEMP","TMP","ProgramData" })
            {
                var value=Environment.GetEnvironmentVariable(key);
                if(value!=null) start.EnvironmentVariables[key]=value;
            }
            start.EnvironmentVariables["DOTNET_ROOT"]=config.DotNetRoot;
            using(var process=Process.Start(start))
            {
                if(process==null)throw new IOException("Knowledge setup did not start.");
                var drain=NativeChatSession.DrainAsync(process.StandardError);
                using(cancel.Register(()=>{try{process.StandardInput.Close();}catch{}}))
                try
                {
                    var bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(request)+"\n");
                    await process.StandardInput.BaseStream.WriteAsync(bytes,0,bytes.Length);
                    await process.StandardInput.BaseStream.FlushAsync();
                    while(true)
                    {
                        var read=NativeChatSession.ReadReadyAsync(process.StandardOutput,131072);
                        if(await Task.WhenAny(read,Task.Delay(TimeSpan.FromMinutes(30)))!=read)throw new IOException("Knowledge setup timed out.");
                        var result=new JavaScriptSerializer().Deserialize<KnowledgeState>(await read);
                        if(result==null)throw new IOException();
                        progress?.Invoke(result);
                        if(result.complete)return result;
                    }
                }
                finally
                {
                    try{process.StandardInput.Close();}catch{}
                    await Task.Run(()=>{try{if(!process.WaitForExit(8000))process.Kill();}catch{}});
                }
            }
        }
    }
}
