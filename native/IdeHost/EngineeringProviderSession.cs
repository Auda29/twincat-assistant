using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TwinCatAssistant.Native
{
    // Optional provider, loaded only from the installer-selected, hash-pinned
    // package. No compiler or vendor interop implementation lives in this host.
    internal sealed class EngineeringProviderSession : IDisposable
    {
        // Change only in a development build; no runtime setting can enable source access.
        internal static readonly bool DevelopmentSourceAccess = false;
        internal const string ProviderEntry = "host/x86/TwinCatMcp.Engineering.Host.dll";
        private readonly EngineeringAuthorization authorization = new EngineeringAuthorization();
        private readonly object provider;
        private readonly MethodInfo dispatch;
        private readonly Type contextType;
        private EngineeringBinding binding;
        private bool approvalsSuspended;
        private readonly Func<object, bool> confirmEditorApproval;
        internal EngineeringBinding Binding { get { return Volatile.Read(ref binding); } }

        internal static bool SupportedHost(HostIdentity host)
        {
            return host != null && host.Kind == "TcXaeShell" && host.ProcessArchitecture == "x86"
                && host.ExecutableVersion == "15.0.0.0" && host.ShellBaseMajor == 15
                && host.TwinCatVersion == "3.1.4024.78"
                && HostTargets.Assess(host).TargetId == "tcxaeshell-x86-tc4024" && HostTargets.Assess(host).AlphaTarget;
        }

        internal static Assembly LoadVerifiedAssembly(string selectedRoot)
        {
            // Configuration alone is not authority to load executable code.
            var hostPath = new Uri(typeof(EngineeringProviderSession).Assembly.CodeBase).LocalPath;
            var root = NativeInstallationSelection.SelectRoot(Path.Combine(Path.GetDirectoryName(hostPath), "assistant-installation.json"));
            if (root == null || !string.Equals(root, selectedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            var manifest = Json().DeserializeObject(new UTF8Encoding(false, true).GetString(ReadRegular(Path.Combine(root, "application-manifest.json"), 32 * 1024 * 1024))) as Dictionary<string, object>;
            object files;
            if (manifest == null || !manifest.TryGetValue("files", out files) || !(files is object[])) throw new InvalidOperationException();
            var matches = ((object[])files).OfType<Dictionary<string, object>>().Where(e => Text(e, "path") == ProviderEntry).ToArray();
            if (matches.Length != 1 || matches[0].Count != 3 || !matches[0].ContainsKey("size")) throw new InvalidOperationException();
            var path = root;
            foreach (var part in ProviderEntry.Split('/'))
            {
                path = Path.Combine(path, part);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException();
            }
            var bytes = ReadRegular(path, 16 * 1024 * 1024);
            if (bytes.LongLength != Convert.ToInt64(matches[0]["size"], CultureInfo.InvariantCulture)) throw new InvalidOperationException();
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != Text(matches[0], "sha256")) throw new InvalidOperationException();
            // Inspect metadata without executing code, then load those exact bytes.
            var identity = Assembly.ReflectionOnlyLoad(bytes).GetName();
            if (identity.Name != "TwinCatMcp.Engineering.Host" || identity.Version != new Version(1, 0, 0, 0)) throw new InvalidOperationException();
            return Assembly.Load(bytes);
        }

        internal EngineeringProviderSession(Assembly assembly, object dte, Func<string, bool?> dirty,
            EngineeringBinding initial, SynchronizationContext ui, Func<string, object> getTextBuffer = null,
            Func<string, bool> navigateExactErrorItem = null, Func<string, string, object> getSectionEditor = null,
            Func<object, bool> confirmEditorApproval = null)
        {
            binding = initial ?? throw new ArgumentNullException(nameof(initial));
            this.confirmEditorApproval = confirmEditorApproval;
            contextType = assembly.GetType("TwinCatMcp.Engineering.Host.HostContext", true);
            var approvalType = assembly.GetType("TwinCatMcp.Engineering.Host.ApprovalRequest", true);
            var factoryType = assembly.GetType("TwinCatMcp.Engineering.Host.EngineeringProviderFactory", true);
            var factory = DevelopmentSourceAccess ? factoryType.GetMethod("CreateWithEditorViews", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(object), typeof(Func<string, bool?>), typeof(Func<>).MakeGenericType(contextType),
                    typeof(Func<,>).MakeGenericType(approvalType, typeof(bool)), typeof(SynchronizationContext), typeof(Func<string, object>), typeof(Func<string, bool>), typeof(Func<string, string, object>) }, null) : null;
            var withEditorViews = factory != null;
            if (factory == null) factory = factoryType.GetMethod("CreateWithHostAccess", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(object), typeof(Func<string, bool?>), typeof(Func<>).MakeGenericType(contextType),
                    typeof(Func<,>).MakeGenericType(approvalType, typeof(bool)), typeof(SynchronizationContext), typeof(Func<string, object>), typeof(Func<string, bool>) }, null);
            var withHostAccess = factory != null;
            if (factory == null && DevelopmentSourceAccess) factory = factoryType.GetMethod("CreateWithEditorAccess", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(object), typeof(Func<string, bool?>), typeof(Func<>).MakeGenericType(contextType),
                    typeof(Func<,>).MakeGenericType(approvalType, typeof(bool)), typeof(SynchronizationContext), typeof(Func<string, object>) }, null);
            var withEditorAccess = factory != null;
            if (factory == null && DevelopmentSourceAccess) factory = factoryType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(object), typeof(Func<string, bool?>), typeof(Func<>).MakeGenericType(contextType),
                    typeof(Func<,>).MakeGenericType(approvalType, typeof(bool)), typeof(SynchronizationContext) }, null);
            if (factory == null) throw new MissingMethodException("Engineering provider factory is unavailable.");
            var current = Expression.Lambda(typeof(Func<>).MakeGenericType(contextType),
                Expression.Convert(Expression.Call(Expression.Constant(this), GetType().GetMethod(nameof(CurrentContext), BindingFlags.NonPublic | BindingFlags.Instance)), contextType)).Compile();
            var argument = Expression.Parameter(approvalType, "request");
            var consume = Expression.Lambda(typeof(Func<,>).MakeGenericType(approvalType, typeof(bool)),
                Expression.Call(Expression.Constant(this), GetType().GetMethod(nameof(ConsumeApproval), BindingFlags.NonPublic | BindingFlags.Instance), Expression.Convert(argument, typeof(object))), argument).Compile();
            provider = factory.Invoke(null, withEditorViews
                ? new object[] { dte, dirty, current, consume, ui, getTextBuffer, navigateExactErrorItem, getSectionEditor }
                : withHostAccess
                ? new object[] { dte, dirty, current, consume, ui, DevelopmentSourceAccess ? getTextBuffer : null, navigateExactErrorItem }
                : withEditorAccess
                ? new object[] { dte, dirty, current, consume, ui, getTextBuffer }
                : new object[] { dte, dirty, current, consume, ui });
            dispatch = assembly.GetType("TwinCatMcp.Engineering.Host.IEngineeringProvider", true).GetMethod("DispatchAsync");
        }

        private object CurrentContext()
        {
            var current = Binding;
            return current == null ? null : Activator.CreateInstance(contextType, current.SessionId, current.IdePid, current.IdeStartedUtc,
                current.SolutionFile, current.ProjectFile, current.ContextEpoch);
        }

        private bool ConsumeApproval(object request)
        {
            try
            {
                if (approvalsSuspended) { authorization.Revoke(); return false; }
                var operation = (string)Property(request, "Operation");
                if (operation != "build.start" && (!DevelopmentSourceAccess || (operation != "source.apply" && operation != "source.rollback"))) return false;
                var expected = Property(request, "Expected");
                var exact = new EngineeringBinding((Guid)Property(expected, "SessionId"), (int)Property(expected, "IdePid"),
                    (DateTime)Property(expected, "IdeStartedUtc"), (string)Property(expected, "SolutionFile"),
                    (string)Property(expected, "ProjectFile"), (string)Property(expected, "ContextEpoch"));
                if ((Guid)Property(request, "SessionId") != exact.SessionId) return false;
                var action = new EngineeringAction(exact, (Guid)Property(request, "OperationId"), operation,
                    (string)Property(request, "TargetObjectId"), (string)Property(request, "Section"),
                    (string)Property(request, "PreviewId"), (string)Property(request, "JournalId"), (DateTime)Property(request, "ExpiresUtc"),
                    (string)Property(request, "CanonicalRequestDigest"), (string)Property(request, "BaselineDigest"), (string)Property(request, "ProposedDigest"));
                if (authorization.Consume(action, Binding, DateTime.UtcNow)) return true;
                if (confirmEditorApproval == null || approvalsSuspended || !exact.Same(Binding)
                    || !confirmEditorApproval(request) || approvalsSuspended || !exact.Same(Binding)
                    || action.ExpiresUtc <= DateTime.UtcNow) return false;
                authorization.Grant(action, DateTime.UtcNow);
                return authorization.Consume(action, Binding, DateTime.UtcNow);
            }
            catch { authorization.Revoke(); return false; }
        }

        internal string CreateRequest(string operation, Guid operationId, object arguments)
        {
            var current = Binding ?? throw new ObjectDisposedException(nameof(EngineeringProviderSession));
            return Json().Serialize(new { protocolVersion = 1, sessionId = current.SessionId.ToString("D"), operationId = operationId.ToString("D"), operation,
                expected = new { idePid = current.IdePid, ideStartedUtc = current.IdeStartedUtc.ToString("o"), solutionFile = current.SolutionFile,
                    projectFile = current.ProjectFile, contextEpoch = current.ContextEpoch }, arguments });
        }

        // Only the explicit native button handler calls this after confirmation.
        internal void ApproveBuild(Guid operation, string exactRequest, DateTime expires)
        {
            if (approvalsSuspended) throw new InvalidOperationException("Project context is being revalidated.");
            var current = Binding ?? throw new ObjectDisposedException(nameof(EngineeringProviderSession));
            authorization.Grant(new EngineeringAction(current, operation, "build.start", current.ProjectFile, null, null, null,
                expires, EngineeringAction.HashRequest(exactRequest), null, null), DateTime.UtcNow);
        }

        // A source grant is created only after the native exact-diff confirmation.
        internal void ApproveSource(string operation, Guid id, string exactRequest, DateTime expires,
            string target, string section, string preview, string journal, string baseline, string proposed)
        {
            if (!DevelopmentSourceAccess || approvalsSuspended || (operation != "source.apply" && operation != "source.rollback")) throw new InvalidOperationException();
            var current = Binding ?? throw new ObjectDisposedException(nameof(EngineeringProviderSession));
            authorization.Grant(new EngineeringAction(current, id, operation, target, section, preview, journal,
                expires, EngineeringAction.HashRequest(exactRequest), baseline, proposed), DateTime.UtcNow);
        }
        internal void RevokeApproval() { authorization.Revoke(); }
        internal void SuspendApprovals() { approvalsSuspended = true; authorization.Revoke(); }
        internal void ResumeApprovals() { approvalsSuspended = false; }

        internal Task<string> DispatchAsync(string request, CancellationToken cancellation)
        {
            if (Binding == null) throw new ObjectDisposedException(nameof(EngineeringProviderSession));
            if (!DevelopmentSourceAccess)
            {
                // Fail closed before invoking the provider, including manual native calls.
                var envelope = Json().DeserializeObject(request) as Dictionary<string, object>;
                var operation = Text(envelope, "operation");
                if (operation == null || operation.StartsWith("source.", StringComparison.Ordinal))
                    throw new InvalidOperationException("Source operations are unavailable in this release.");
            }
            return (Task<string>)dispatch.Invoke(provider, new object[] { request, cancellation });
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref binding, null);
            authorization.Revoke();
            try { (provider as IDisposable)?.Dispose(); }
            catch { /* A provider cleanup error must not retain authorization or break chat. */ }
        }
        private static object Property(object value, string name) { return value.GetType().GetProperty(name).GetValue(value, null); }
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 32 }; }
        internal static string Text(Dictionary<string, object> value, string key) { object found; return value != null && value.TryGetValue(key, out found) ? found as string : null; }
        private static byte[] ReadRegular(string path, int limit)
        {
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || info.Length > limit) throw new IOException();
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var bytes = new MemoryStream())
            {
                var buffer = new byte[8192]; int count;
                while ((count = file.Read(buffer, 0, buffer.Length)) > 0)
                { if (bytes.Length + count > limit) throw new IOException(); bytes.Write(buffer, 0, count); }
                return bytes.ToArray();
            }
        }
    }
}
