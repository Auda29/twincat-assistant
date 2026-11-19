using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TwinCatAssistant.Native;

namespace EnvDTE {
    public interface DTE { }
    public interface SolutionConfiguration { }
    public class Project { }
}
namespace EnvDTE80 {
    public interface SolutionConfiguration2 : EnvDTE.SolutionConfiguration { }
}
namespace TCatSysManagerLib {
    public interface ITcSysManager { }
    public interface ITcSmTreeItem { }
    public interface PrivateTestInterface { }
}

// Only dependencies of unused host-loading/eligibility paths are stubbed. The
// constructor and authorization under test are compiled from production source.
namespace TwinCatAssistant.Native {
    internal sealed class HostIdentity { public string Kind, ProcessArchitecture, TwinCatVersion, ExecutableVersion; public int ShellBaseMajor; }
    internal sealed class HostAssessment { public string TargetId; public bool AlphaTarget; }
    internal static class HostTargets { public static HostAssessment Assess(HostIdentity h) { throw new Exception("No host access"); } }
    internal static class NativeInstallationSelection { public static string SelectRoot(string p) { throw new Exception("No installed state access"); } }
}
namespace TwinCatMcp.Engineering.Host {
    public sealed class HostContext {
        public HostContext(Guid session, int pid, DateTime started, string solution, string project, string epoch) { }
    }
    public sealed class ApprovalRequest { }
    public interface IEngineeringProvider { Task<string> DispatchAsync(string request, CancellationToken cancellation); }
    public sealed class ProbeProvider : IEngineeringProvider {
        public Task<string> DispatchAsync(string r, CancellationToken c) { throw new Exception("No provider operation"); }
    }
    public static class EngineeringProviderFactory {
        public static int Calls7, Calls9;
        public static Func<string,Type> Received;
        public static object Buffer, Section;
        public static IEngineeringProvider CreateWithHostAccess(object d, Func<string,bool?> dirty, Func<HostContext> current,
            Func<ApprovalRequest,bool> approval, SynchronizationContext ui, Func<string,object> buffer, Func<string,bool> nav) {
            Calls7++; Buffer=buffer; return new ProbeProvider();
        }
#if !LEGACY_ONLY
        public static IEngineeringProvider CreateWithInteropTypes(object d, Func<string,bool?> dirty, Func<HostContext> current,
            Func<ApprovalRequest,bool> approval, SynchronizationContext ui, Func<string,object> buffer, Func<string,bool> nav,
            Func<string,string,object> section, Func<string,Type> types) {
            Calls9++; Received=types; Buffer=buffer; Section=section;
#if FAIL_FACTORY9
            throw new InvalidOperationException("Synthetic factory failure");
#else
            return new ProbeProvider();
#endif
        }
#endif
    }
}
internal static class EngineeringFactoryProbe {
    private static int checks;
    private static void Check(bool value) { if(!value) throw new Exception("Factory contract assertion failed"); checks++; }
    public static int Main(string[] args) {
        if(args.Length!=0) return ActualCandidate(args);
        var duplicate = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("DuplicateInterop"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("DuplicateInterop");
        duplicate.DefineType("EnvDTE.DTE", TypeAttributes.Interface | TypeAttributes.Public | TypeAttributes.Abstract).CreateType();
        var captured = EngineeringInteropReference.Capture();
        Check(captured("EnvDTE.DTE") == typeof(EnvDTE.DTE));
        Check(captured("EnvDTE80.SolutionConfiguration2") == typeof(EnvDTE80.SolutionConfiguration2));
        Check(captured("TCatSysManagerLib.ITcSysManager") == typeof(TCatSysManagerLib.ITcSysManager));
        Check(captured("EnvDTE.Project") == null);
        Check(captured("TCatSysManagerLib.PrivateTestInterface") == null);
        Check(captured("envdte.dte") == null);
        var assembly=Assembly.GetExecutingAssembly();
        var binding=new EngineeringBinding(Guid.NewGuid(),123,DateTime.UtcNow,"C:\\Synthetic.sln","C:\\Synthetic.plcproj","1");
        Func<string,Type> map=name=>{ throw new Exception("Host must not eagerly query binding"); };
        Func<string,object> buffer=name=>{ throw new Exception("Source must remain dormant"); };
        Func<string,string,object> section=(name,part)=>{ throw new Exception("Source must remain dormant"); };
        Func<EngineeringProviderSession> create=()=>new EngineeringProviderSession(assembly,new object(),p=>false,binding,
            new SynchronizationContext(),buffer,p=>false,section,null,map);
        try {
            new EngineeringProviderSession(assembly,new object(),p=>false,binding,
                new SynchronizationContext(),buffer,p=>false,section,sourceAccess: true);
            throw new Exception("Legacy source access was accepted");
        }
        catch(ArgumentException e) { Check(e.ParamName == "sourceAccess"); }
        try {
            new EngineeringProviderSession(assembly,new object(),p=>false,binding,
                new SynchronizationContext(),navigateExactErrorItem: p=>false,getInteropType:map,sourceAccess: true);
            throw new Exception("Source access without an XML accessor was accepted");
        }
        catch(ArgumentException e) { Check(e.ParamName == "sourceAccess"); }
        using(var legacy=new EngineeringProviderSession(assembly,new object(),p=>false,binding,new SynchronizationContext(),buffer,p=>false)) {
            Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Calls7==1);
            Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Buffer==null);
        }
#if LEGACY_ONLY
        try { create(); throw new Exception("Missing factory9 was accepted"); }
        catch(MissingMethodException) { checks++; }
#elif FAIL_FACTORY9
        try { create(); throw new Exception("Factory9 failure was ignored"); }
        catch(TargetInvocationException e) { Check(e.InnerException is InvalidOperationException); }
        Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Calls9==1);
#else
        using(var selected=create()) {
            Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Calls9==1);
            Check(object.ReferenceEquals(map,TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Received));
            Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Buffer==null);
            Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Section==null);
        }
        using(var editor=new EngineeringProviderSession(assembly,new object(),p=>false,binding,
            new SynchronizationContext(),buffer,p=>false,section,null,map,sourceAccess: true)) {
            Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Calls9==2);
            Check(object.ReferenceEquals(buffer,TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Buffer));
            Check(object.ReferenceEquals(section,TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Section));
        }
#endif
        Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Calls7==1);
        Console.WriteLine("PASS explicit factory contract: "+checks+" checks; no IDE/provider operations");
        return 0;
    }
    private static int ActualCandidate(string[] args) {
        Check(args.Length==2);
        var bytes=File.ReadAllBytes(args[0]);
        using(var sha=SHA256.Create()) Check(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()==args[1]);
        var candidate=Assembly.Load(bytes);
        var binding=new EngineeringBinding(Guid.NewGuid(),123,DateTime.UtcNow,"C:\\Synthetic.sln","C:\\Synthetic.plcproj","1");
        var requested=new HashSet<string>(StringComparer.Ordinal); int calls=0;
        Func<string,Type> map=name=>{ calls++; Check(requested.Add(name)); return null; };
        Func<string,object> buffer=name=>{throw new Exception("Source callback invoked");};
        Func<string,string,object> section=(name,part)=>{throw new Exception("Section callback invoked");};
        using(var session=new EngineeringProviderSession(candidate,new object(),p=>false,binding,new SynchronizationContext(),buffer,p=>false,section,null,map)) {
            Check(calls==29);
            Check(requested.Contains("TCatSysManagerLib.ITcSysManager") && requested.Contains("EnvDTE80.SolutionConfiguration2"));
            for(int i=0;i<2;i++) {
                var operation=Guid.NewGuid();
                var request=session.CreateRequest("session.ping",operation,new {});
                var response=EngineeringProviderSession.Json().DeserializeObject(session.DispatchAsync(request,CancellationToken.None).GetAwaiter().GetResult()) as Dictionary<string,object>;
                Check(response!=null && (string)response["status"]=="succeeded");
                Check((string)response["sessionId"]==binding.SessionId.ToString("D") && (string)response["operationId"]==operation.ToString("D"));
            }
            var describe=session.CreateRequest("session.describe",Guid.NewGuid(),new {});
            var detail=EngineeringProviderSession.Json().DeserializeObject(session.DispatchAsync(describe,CancellationToken.None).GetAwaiter().GetResult()) as Dictionary<string,object>;
            Check((string)detail["status"]=="succeeded");
            var result=(Dictionary<string,object>)detail["result"];
            var caps=(Dictionary<string,object>)result["capabilities"];
            Check((string)caps["plcBuild"]=="unsupported");
            foreach(var key in new[]{"sourceRead","sourcePreview","sourceApply","sourceRollback"}) Check((string)caps[key]=="unsupported");
            var source=session.CreateRequest("source.read",Guid.NewGuid(),new {});
            try { session.DispatchAsync(source,CancellationToken.None); throw new Exception("Source request accepted"); }
            catch(InvalidOperationException) {checks++;}
            Check(calls==29);
        }
        Console.WriteLine("PASS actual candidate factory9 host wire: "+checks+" assertions, all 29 bindings unavailable by design; no COM/IDE/build/source operation");
        return 0;
    }
}
