using System;
using System.IO;
using System.Xml.Linq;
using System.Linq;
namespace TwinCatAssistant.Native
{
    public static class BuildProjectPolicy
    {
        public static void Validate(string file)
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 1048576 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
            if (File.Exists(file + ".user")) throw new InvalidOperationException();
            var xml = XDocument.Load(file);
            if (xml.Root == null || xml.Root.Name.LocalName != "Project" || xml.Root.Attribute("Sdk") != null
                || xml.Root.Attribute("InitialTargets") != null || (xml.Root.Attribute("DefaultTargets") != null && (string)xml.Root.Attribute("DefaultTargets") != "Build")) throw new InvalidOperationException();
            if (xml.Descendants().Count(node => node.Name.LocalName == "Import") != 1) throw new InvalidOperationException();
            foreach (var node in xml.Descendants())
            {
                var name = node.Name.LocalName;
                if (name == "Sdk" || name.IndexOf("DependsOn", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.EndsWith("Targets", StringComparison.OrdinalIgnoreCase) || name.StartsWith("MSBuildExtensionsPath", StringComparison.OrdinalIgnoreCase)
                    || name == "DirectoryBuildPropsPath" || name == "DirectoryBuildTargetsPath") throw new InvalidOperationException();
                if (name == "ProjectReference" || name == "ImportGroup" || name == "Target" || name == "UsingTask" || name == "PreBuildEvent" || name == "PostBuildEvent" || name == "BuildDependsOn") throw new InvalidOperationException();
                if (name == "Import" && (string)node.Attribute("Project") != @"$(MSBuildExtensionsPath)\Beckhoff\TwinCAT\v3.1\TcPlcProject.targets") throw new InvalidOperationException();
            }
            for (var dir = info.Directory; dir != null; dir = dir.Parent)
            {
                if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
                foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Build.rsp" })
                    if (File.Exists(Path.Combine(dir.FullName, name))) throw new InvalidOperationException();
            }
        }
    }
}
