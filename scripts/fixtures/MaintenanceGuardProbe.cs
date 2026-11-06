using System;
using System.Reflection;

// Synthetic configuration metadata probe only. Never activates a VS package,
// creates an IDE, starts a backend, or writes credentials/preferences.
public sealed class MaintenanceGuardProbe : MarshalByRefObject
{
    public override object InitializeLifetimeService() { return null; }
    private Assembly Host(string path) { return Assembly.LoadFrom(path); }
    public string[] Identity(string path)
    {
        var assembly = Host(path);
        return new[] { assembly.CodeBase, assembly.Location };
    }
    public string Read(string path, string property)
    {
        return (string)Host(path).GetType("TwinCatAssistant.Native.NativeChatConfiguration", true)
            .GetProperty(property).GetValue(null, null);
    }
    public string LoadState(string path)
    {
        var type = Host(path).GetType("TwinCatAssistant.Native.NativeChatConfiguration", true);
        var config = type.GetMethod("Load").Invoke(null, null);
        return (string)type.GetProperty("StateRoot").GetValue(config, null);
    }
    public void ConstructDefault(string path, string typeName)
    {
        Host(path).GetType("TwinCatAssistant.Native." + typeName, true)
            .GetConstructor(new[] { typeof(string) }).Invoke(new object[] { null });
    }
}
