using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TwinCatAssistant.Native
{
    // Capture exact runtime types resolved from the host's own compiler
    // references before constructing the provider. The callback cannot load
    // interop or select a type from an unrelated assembly in the AppDomain.
    internal static class EngineeringInteropReference
    {
        private static readonly string[] DteNames = {
            "DTE", "Events", "_dispBuildEvents_Event", "Solution", "Projects",
            "Project", "ProjectItems", "ProjectItem", "Documents", "Document",
            "SolutionBuild", "SolutionConfiguration", "SolutionContexts", "SolutionContext",
            "BuildDependencies", "BuildDependency", "Windows", "Window", "OutputWindow",
            "OutputWindowPanes", "OutputWindowPane", "TextDocument", "TextPoint", "EditPoint"
        };

        private static readonly string[] TwinCatNames = {
            "ITcSysManager", "ITcSmTreeItem", "ITcSysManager7", "ITcConfigManager",
            "ITcProjectRoot", "ITcPlcDeclaration", "ITcPlcImplementation"
        };

        internal static Func<string, Type> Capture()
        {
            var types = new Dictionary<string, Type>(StringComparer.Ordinal);
            CaptureFamily(types, typeof(EnvDTE.DTE).Assembly, "EnvDTE.", DteNames);
            CaptureFamily(types, typeof(TCatSysManagerLib.ITcSysManager).Assembly,
                "TCatSysManagerLib.", TwinCatNames);

#if HOST_X86
            Type configuration;
            if (types.TryGetValue("EnvDTE.SolutionConfiguration", out configuration))
            {
                var platform = PlatformInterface(configuration);
                if (platform != null) types.Add("EnvDTE80.SolutionConfiguration2", platform);
            }
#endif

            return name =>
            {
                Type type;
                return name != null && types.TryGetValue(name, out type) ? type : null;
            };
        }

        private static void CaptureFamily(Dictionary<string, Type> types, Assembly reference,
            string prefix, IEnumerable<string> names)
        {
            if (reference.IsDynamic || reference.ReflectionOnly) return;
            foreach (var name in names)
            {
                var fullName = prefix + name;
                Type type;
                try { type = reference.GetType(fullName, false); }
                catch (Exception e) when (e is TypeLoadException || e is FileNotFoundException
                    || e is FileLoadException || e is BadImageFormatException) { continue; }
                if (type != null && ReferenceEquals(type.Assembly, reference) && OrdinalFullName(type, fullName))
                    types.Add(fullName, type);
            }
        }

        private static bool OrdinalFullName(Type type, string fullName)
        {
            return type.IsInterface && type.IsPublic && !type.IsGenericType
                && string.Equals(type.FullName, fullName, StringComparison.Ordinal);
        }

#if HOST_X86
        private static Type PlatformInterface(Type configuration)
        {
            try
            {
                var type = ResolveCompiledPlatformInterface();
                // The platform PIA must refer to the runtime configuration
                // interface that the existing host has bound.
                return OrdinalFullName(type, "EnvDTE80.SolutionConfiguration2")
                    && Array.Exists(type.GetInterfaces(), parent => ReferenceEquals(parent, configuration))
                    ? type : null;
            }
            catch (Exception e) when (e is TypeLoadException || e is FileNotFoundException
                || e is FileLoadException || e is BadImageFormatException) { return null; }
        }

        // Keep optional reference resolution inside its failure boundary even
        // when the CLR must bind the assembly during method compilation.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Type ResolveCompiledPlatformInterface()
        {
            return typeof(EnvDTE80.SolutionConfiguration2);
        }
#endif
    }
}
