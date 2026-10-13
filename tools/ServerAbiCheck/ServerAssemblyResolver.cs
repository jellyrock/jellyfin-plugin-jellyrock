using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace ServerAbiCheck;

/// <summary>
/// Resolves assemblies from one server directory only. Cecil's default resolver falls back to the
/// running process's own framework, which would let a reference the server lacks resolve against this
/// tool's runtime instead. A Jellyfin release ships self-contained, so its directory holds everything
/// the plugin loader binds against, the .NET runtime and ASP.NET Core included.
/// </summary>
internal sealed class ServerAssemblyResolver : IAssemblyResolver
{
    private readonly string _directory;
    private readonly Dictionary<string, AssemblyDefinition?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerAssemblyResolver"/> class.
    /// </summary>
    /// <param name="directory">The server directory holding its <c>.dll</c> files.</param>
    public ServerAssemblyResolver(string directory)
    {
        _directory = directory;
    }

    /// <inheritdoc />
    public AssemblyDefinition? Resolve(AssemblyNameReference name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Resolve(name, new ReaderParameters());
    }

    /// <inheritdoc />
    public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_cache.TryGetValue(name.Name, out var cached))
        {
            return cached;
        }

        // Matched by name only: the server binds a plugin's references to whatever version it ships
        // (a plugin built against 10.11.0 loads on 12.x), so a version difference is not a break.
        var path = Path.Combine(_directory, name.Name + ".dll");
        AssemblyDefinition? assembly = null;
        if (File.Exists(path))
        {
            assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = this });
        }

        _cache[name.Name] = assembly;
        return assembly;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var assembly in _cache.Values)
        {
            assembly?.Dispose();
        }

        _cache.Clear();
    }
}
