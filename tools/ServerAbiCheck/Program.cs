using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Mono.Cecil;

namespace ServerAbiCheck;

/// <summary>
/// Entry point: <c>ServerAbiCheck &lt;plugin.dll&gt; &lt;server-dir&gt;</c>. Exit 0 when the plugin
/// binds, 1 when it does not (each problem printed), 2 when the inputs cannot be checked.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: ServerAbiCheck <plugin.dll> <server-dir>");
            return 2;
        }

        var pluginPath = args[0];
        var serverDirectory = args[1];
        if (!File.Exists(pluginPath))
        {
            Console.Error.WriteLine("error: no plugin assembly at " + pluginPath);
            return 2;
        }

        var serverAssembly = Path.Combine(serverDirectory, "jellyfin.dll");
        if (!File.Exists(serverAssembly))
        {
            Console.Error.WriteLine("error: no jellyfin.dll in " + serverDirectory + ", so it is not a Jellyfin server directory");
            return 2;
        }

        using var resolver = new ServerAssemblyResolver(serverDirectory);
        using var plugin = ModuleDefinition.ReadModule(pluginPath, new ReaderParameters { AssemblyResolver = resolver });
        var report = AbiChecker.Check(plugin);

        // A check that read nothing would pass anything: treat it as unable to check, not as a pass.
        if (report.TypeReferences == 0 || report.MemberReferences == 0)
        {
            Console.Error.WriteLine("error: found no type or member references in " + pluginPath);
            return 2;
        }

        var serverVersion = AssemblyName.GetAssemblyName(serverAssembly).Version;
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"server {serverVersion}: {report.TypeReferences} type refs, {report.MemberReferences} member refs, {report.ImplementationChecks} implementation checks, {report.Problems.Count} problems"));
        foreach (var problem in report.Problems)
        {
            Console.WriteLine("  " + problem);
        }

        return report.Problems.Count == 0 ? 0 : 1;
    }
}
