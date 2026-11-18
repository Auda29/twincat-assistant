using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TwinCatAssistant.Native;

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
    public static int Main() {
        var assembly=Assembly.GetExecutingAssembly();
        var binding=new EngineeringBinding(Guid.NewGuid(),123,DateTime.UtcNow,"C:\\Synthetic.sln","C:\\Synthetic.plcproj","1");
        Func<string,Type> map=name=>{ throw new Exception("Host must not eagerly query binding"); };
        Func<string,object> buffer=name=>{ throw new Exception("Source must remain dormant"); };
        Func<string,string,object> section=(name,part)=>{ throw new Exception("Source must remain dormant"); };
        Func<EngineeringProviderSession> create=()=>new EngineeringProviderSession(assembly,new object(),p=>false,binding,
            new SynchronizationContext(),buffer,p=>false,section,null,map);
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
#endif
        Check(TwinCatMcp.Engineering.Host.EngineeringProviderFactory.Calls7==1);
        Console.WriteLine("PASS explicit factory contract: "+checks+" checks; no IDE/provider operations");
        return 0;
    }
}
