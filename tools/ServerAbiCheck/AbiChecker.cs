using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;

namespace ServerAbiCheck;

/// <summary>
/// Checks a plugin module against the server its resolver reads, the way the CLR binds it at load:
/// <list type="bullet">
/// <item>every referenced assembly, type, method and field exists (a missing one is a
/// <c>TypeLoadException</c> or <c>MissingMethodException</c>);</item>
/// <item>every concrete plugin type still implements every member of the server interfaces and
/// abstract classes it derives from (an added interface member is a <c>TypeLoadException</c> the
/// moment the type loads);</item>
/// <item>every plugin <c>override</c> still has a server member to override (a removed one loads
/// fine, but the plugin's method is then never called).</item>
/// </list>
/// Types are compared by full name, so a type the server moved behind a type forwarder still matches;
/// generic arguments are substituted level by level, so <c>BasePlugin&lt;PluginConfiguration&gt;</c>
/// is compared with its argument filled in.
/// </summary>
internal static class AbiChecker
{
    /// <summary>
    /// Runs every check.
    /// </summary>
    /// <param name="plugin">The plugin module, read with a <see cref="ServerAssemblyResolver"/>.</param>
    /// <returns>The counts and the problems found.</returns>
    public static AbiReport Check(ModuleDefinition plugin)
    {
        var report = new AbiReport();
        CheckAssemblyReferences(plugin, report);
        CheckTypeReferences(plugin, report);
        CheckMemberReferences(plugin, report);
        foreach (var type in plugin.GetTypes())
        {
            if (type.Name != "<Module>" && !type.IsInterface)
            {
                CheckImplementations(plugin, type, report);
            }
        }

        return report;
    }

    private static void CheckAssemblyReferences(ModuleDefinition plugin, AbiReport report)
    {
        foreach (var reference in plugin.AssemblyReferences)
        {
            if (plugin.AssemblyResolver.Resolve(reference) is null)
            {
                report.Add("assembly not on the server: " + reference.Name);
            }
        }
    }

    private static void CheckTypeReferences(ModuleDefinition plugin, AbiReport report)
    {
        foreach (var reference in plugin.GetTypeReferences())
        {
            report.TypeReferences++;
            if (reference.Resolve() is null)
            {
                report.Add("type not found: " + reference.FullName + " [" + reference.Scope.Name + "]");
            }
        }
    }

    private static void CheckMemberReferences(ModuleDefinition plugin, AbiReport report)
    {
        foreach (var reference in plugin.GetMemberReferences())
        {
            var declaring = reference.DeclaringType;

            // Array methods (Get, Set, Address, the constructor) are made by the runtime and have no
            // definition to find; a generic parameter's members are bound through its constraint.
            if (declaring is ArrayType || declaring.GetElementType() is GenericParameter)
            {
                continue;
            }

            report.MemberReferences++;

            // A member of a type that is itself missing is already reported with the type.
            if (declaring.Resolve() is null)
            {
                continue;
            }

            var found = reference switch
            {
                MethodReference method => method.Resolve() is not null,
                FieldReference field => field.Resolve() is not null,
                _ => true,
            };
            if (!found)
            {
                report.Add("member not found: " + reference.FullName);
            }
        }
    }

    private static void CheckImplementations(ModuleDefinition plugin, TypeDefinition type, AbiReport report)
    {
        var chain = BaseChain(plugin, type, report);
        if (chain is null)
        {
            return;
        }

        CheckOverridesHaveBase(plugin, chain, report);
        if (type.IsAbstract)
        {
            return;
        }

        var interfaces = Interfaces(plugin, type, chain, report);
        foreach (var (definition, view) in interfaces)
        {
            if (definition.Module == plugin)
            {
                continue;
            }

            foreach (var member in definition.Methods.Where(m => m.IsAbstract))
            {
                report.ImplementationChecks++;
                if (!ImplementsInterfaceMember(chain, interfaces, member, view))
                {
                    report.Add("not implemented: " + type.FullName + " lacks " + view.FullName + "::" + Describe(member, view));
                }
            }
        }

        for (var level = 1; level < chain.Count; level++)
        {
            var (definition, view) = chain[level];
            if (definition.Module == plugin)
            {
                continue;
            }

            foreach (var member in definition.Methods.Where(m => m.IsAbstract))
            {
                report.ImplementationChecks++;
                if (!OverriddenBelow(chain, level, member))
                {
                    report.Add("not implemented: " + type.FullName + " lacks abstract " + view.FullName + "::" + Describe(member, view));
                }
            }
        }
    }

    /// <summary>
    /// The type and its base classes, most derived first, each paired with how the type sees it (its
    /// generic arguments filled in). Null when a base is missing, which is reported as a type
    /// reference, or cannot be derived from.
    /// </summary>
    private static List<(TypeDefinition Definition, TypeReference View)>? BaseChain(ModuleDefinition plugin, TypeDefinition type, AbiReport report)
    {
        var chain = new List<(TypeDefinition Definition, TypeReference View)> { (type, type) };
        TypeDefinition definition = type;
        TypeReference view = type;
        while (definition.BaseType is not null)
        {
            var baseView = Substitute(definition.BaseType, view);
            var baseDefinition = baseView.Resolve();
            if (baseDefinition is null)
            {
                return null;
            }

            if (baseDefinition.Module != plugin && (baseDefinition.IsInterface || baseDefinition.IsSealed))
            {
                report.Add("cannot derive: " + type.FullName + " derives from " + baseView.FullName + ", which the server made " + (baseDefinition.IsInterface ? "an interface" : "sealed"));
                return null;
            }

            chain.Add((baseDefinition, baseView));
            definition = baseDefinition;
            view = baseView;
        }

        return chain;
    }

    /// <summary>
    /// Every interface the type implements, directly, through a base class or through another
    /// interface, each with its generic arguments filled in.
    /// </summary>
    private static List<(TypeDefinition Definition, TypeReference View)> Interfaces(
        ModuleDefinition plugin,
        TypeDefinition type,
        List<(TypeDefinition Definition, TypeReference View)> chain,
        AbiReport report)
    {
        var interfaces = new List<(TypeDefinition Definition, TypeReference View)>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        void Add(TypeReference view)
        {
            if (!seen.Add(Key(view)))
            {
                return;
            }

            var definition = view.Resolve();
            if (definition is null)
            {
                return;
            }

            if (!definition.IsInterface)
            {
                if (definition.Module != plugin)
                {
                    report.Add("cannot implement: " + type.FullName + " implements " + view.FullName + ", which the server made a class");
                }

                return;
            }

            interfaces.Add((definition, view));
            foreach (var inherited in definition.Interfaces)
            {
                Add(Substitute(inherited.InterfaceType, view));
            }
        }

        foreach (var (definition, view) in chain)
        {
            foreach (var implemented in definition.Interfaces)
            {
                Add(Substitute(implemented.InterfaceType, view));
            }
        }

        return interfaces;
    }

    /// <summary>
    /// Whether a plugin <c>override</c> still overrides something: a virtual method of the same
    /// signature in a base class. One with an explicit <c>.override</c> (a covariant return) is bound
    /// through a member reference, which is checked there.
    /// </summary>
    private static void CheckOverridesHaveBase(ModuleDefinition plugin, List<(TypeDefinition Definition, TypeReference View)> chain, AbiReport report)
    {
        // Only the type itself: its base classes in the plugin are checked as types of their own.
        var (definition, view) = chain[0];
        if (definition.Module != plugin)
        {
            return;
        }

        foreach (var method in definition.Methods)
        {
            if (!method.IsVirtual || method.IsNewSlot || method.IsStatic || method.HasOverrides)
            {
                continue;
            }

            report.ImplementationChecks++;
            var found = chain.Skip(1).Any(level => level.Definition.Methods.Any(
                candidate => candidate.IsVirtual && SameSignature(method, view, candidate, level.View)));
            if (!found)
            {
                report.Add("override has no base: " + definition.FullName + "::" + Describe(method, view));
            }
        }
    }

    private static bool ImplementsInterfaceMember(
        List<(TypeDefinition Definition, TypeReference View)> chain,
        List<(TypeDefinition Definition, TypeReference View)> interfaces,
        MethodDefinition member,
        TypeReference interfaceView)
    {
        var interfaceKey = Key(interfaceView);
        foreach (var (definition, view) in chain)
        {
            foreach (var method in definition.Methods)
            {
                // Explicit implementation (and every compiler-generated one): an .override directive.
                if (method.HasOverrides && method.Overrides.Any(o => o.Resolve() == member && Key(Substitute(o.DeclaringType, view)) == interfaceKey))
                {
                    return true;
                }

                // Implicit implementation: a public method of the same name and signature, virtual
                // for an instance member (the compiler marks it so), static for a static abstract one.
                var kindMatches = member.IsStatic
                    ? method.IsStatic
                    : !method.IsStatic && method.IsVirtual;
                if (kindMatches && method.IsPublic && SameSignature(method, view, member, interfaceView))
                {
                    return true;
                }
            }
        }

        // A default implementation another interface provides for this one.
        foreach (var (definition, view) in interfaces)
        {
            foreach (var method in definition.Methods)
            {
                if (!method.IsAbstract && method.HasOverrides && method.Overrides.Any(o => o.Resolve() == member && Key(Substitute(o.DeclaringType, view)) == interfaceKey))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool OverriddenBelow(List<(TypeDefinition Definition, TypeReference View)> chain, int level, MethodDefinition member)
    {
        var memberView = chain[level].View;
        for (var below = 0; below < level; below++)
        {
            var (definition, view) = chain[below];
            foreach (var method in definition.Methods)
            {
                if (method.HasOverrides && method.Overrides.Any(o => o.Resolve() == member))
                {
                    return true;
                }

                if (method.IsVirtual && !method.IsNewSlot && SameSignature(method, view, member, memberView))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SameSignature(MethodDefinition a, TypeReference aView, MethodDefinition b, TypeReference bView)
    {
        if (a.Name != b.Name
            || a.Parameters.Count != b.Parameters.Count
            || a.GenericParameters.Count != b.GenericParameters.Count
            || Key(Substitute(a.ReturnType, aView)) != Key(Substitute(b.ReturnType, bView)))
        {
            return false;
        }

        for (var i = 0; i < a.Parameters.Count; i++)
        {
            if (Key(Substitute(a.Parameters[i].ParameterType, aView)) != Key(Substitute(b.Parameters[i].ParameterType, bView)))
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(MethodDefinition method, TypeReference view)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => Substitute(p.ParameterType, view).FullName));
        return Substitute(method.ReturnType, view).FullName + " " + method.Name + "(" + parameters + ")";
    }

    /// <summary>
    /// Fills a type's own generic parameters in with the arguments its view carries, so a member of
    /// <c>IEnumerable&lt;T&gt;</c> seen through <c>IEnumerable&lt;string&gt;</c> reads as <c>string</c>.
    /// </summary>
    private static TypeReference Substitute(TypeReference type, TypeReference view)
    {
        if (view is not GenericInstanceType context)
        {
            return type;
        }

        switch (type)
        {
            case GenericParameter { Type: GenericParameterType.Type } parameter when parameter.Position < context.GenericArguments.Count:
                return context.GenericArguments[parameter.Position];
            case GenericInstanceType instance:
                var substituted = new GenericInstanceType(instance.ElementType);
                foreach (var argument in instance.GenericArguments)
                {
                    substituted.GenericArguments.Add(Substitute(argument, view));
                }

                return substituted;
            case ArrayType array:
                return new ArrayType(Substitute(array.ElementType, view), array.Rank);
            case ByReferenceType byReference:
                return new ByReferenceType(Substitute(byReference.ElementType, view));
            case PointerType pointer:
                return new PointerType(Substitute(pointer.ElementType, view));
            case RequiredModifierType required:
                return new RequiredModifierType(required.ModifierType, Substitute(required.ElementType, view));
            case OptionalModifierType optional:
                return new OptionalModifierType(optional.ModifierType, Substitute(optional.ElementType, view));
            default:
                return type;
        }
    }

    /// <summary>
    /// A comparable form of a type: its full name without the assembly, so a forwarded type matches,
    /// and generic parameters by position, so <c>T</c> in one declaration matches <c>TItem</c> in another.
    /// </summary>
    private static string Key(TypeReference type)
    {
        return type switch
        {
            GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position.ToString(CultureInfo.InvariantCulture),
            GenericInstanceType instance => Key(instance.ElementType) + "<" + string.Join(",", instance.GenericArguments.Select(Key)) + ">",
            ArrayType array => Key(array.ElementType) + "[" + array.Rank.ToString(CultureInfo.InvariantCulture) + "]",
            ByReferenceType byReference => Key(byReference.ElementType) + "&",
            PointerType pointer => Key(pointer.ElementType) + "*",
            RequiredModifierType required => Key(required.ElementType) + " modreq(" + required.ModifierType.FullName + ")",
            OptionalModifierType optional => Key(optional.ElementType) + " modopt(" + optional.ModifierType.FullName + ")",
            _ => type.FullName,
        };
    }
}
