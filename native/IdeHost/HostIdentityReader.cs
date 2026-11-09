using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace TwinCatAssistant.Native
{
    // Reads the loading IDE's identity once, without COM or IDE services.
    internal static class HostIdentityReader
    {
        private static HostIdentity _current;

        public static HostIdentity Current
        {
            get { return _current ?? (_current = Read()); }
        }

        private static HostIdentity Read()
        {
            var identity = new HostIdentity { ProcessArchitecture = IntPtr.Size == 8 ? "amd64" : "x86" };
            try
            {
                string path;
                using (var process = Process.GetCurrentProcess()) path = process.MainModule.FileName;
                identity.ExecutableName = Path.GetFileName(path);
                identity.ExecutableVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
                var msenv = Path.Combine(Path.GetDirectoryName(path), "msenv.dll");
                if (File.Exists(msenv)) identity.ShellBaseVersion = FileVersionInfo.GetVersionInfo(msenv).FileVersion;
            }
            catch (Exception) { /* Unknown values stay null. */ }
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = hive.OpenSubKey(@"SOFTWARE\Beckhoff\TwinCAT3\System", false))
                    {
                        var value = key?.GetValue("TcVersion") as string;
                        if (!string.IsNullOrWhiteSpace(value)) { identity.TwinCatVersion = value.Trim(); break; }
                    }
                }
                catch (Exception) { }
            }
            return identity;
        }
    }
}
