using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using MapperForge.Generator.Models;
using MapperForge.Generator.Utilities;

namespace MapperForge.Generator.Parsing;

internal static class ExternalMappingCatalog
{
    public static Dictionary<MappingPair, MappingEntry> Discover(Compilation compilation)
    {
        var entries = new Dictionary<MappingPair, MappingEntry>();
        var attributeType = compilation.GetTypeByMetadataName("MapperForge.MapperForgeMappingAttribute");
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols
            .Distinct(SymbolEqualityComparer.Default).OfType<IAssemblySymbol>()
            .OrderBy(static assembly => assembly.Identity.ToString(), StringComparer.Ordinal))
        {
            foreach (var attribute in assembly.GetAttributes().Where(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType)))
            {
                var args = attribute.ConstructorArguments;
                // Without a recognizable pair, no generated plan can require this entry.
                if (args.Length < 2 || args[0].Value is not ITypeSymbol source ||
                    args[1].Value is not INamedTypeSymbol destination) continue;
                var pair = new MappingPair(source, destination);
                var dependencies = ImmutableArray.CreateBuilder<MappingPair>();
                string? reason = null;
                if (args.Length != 4 || args[2].Value is not int version || version != 1)
                    reason = "unsupported contract version or attribute format";
                else if (args[3].Kind != TypedConstantKind.Array || args[3].IsNull || args[3].Values.Length % 2 != 0)
                    reason = "dependency types must alternate source and destination";
                else
                {
                    var seen = new HashSet<MappingPair>();
                    for (var i = 0; i < args[3].Values.Length; i += 2)
                    {
                        if (args[3].Values[i].Value is not INamedTypeSymbol dependencySource ||
                            args[3].Values[i + 1].Value is not INamedTypeSymbol dependencyDestination ||
                            SymbolUtilities.IsOpenType(dependencySource) || SymbolUtilities.IsOpenType(dependencyDestination))
                        {
                            reason = "dependency pairs must contain closed named types";
                            break;
                        }
                        var dependency = new MappingPair(dependencySource, dependencyDestination);
                        if (!seen.Add(dependency))
                        {
                            reason = "duplicate dependency pair";
                            break;
                        }
                        dependencies.Add(dependency);
                    }
                    if (reason is null && !dependencies.SequenceEqual(dependencies.OrderBy(static pair => pair.StableIdentity, StringComparer.Ordinal)))
                        reason = "dependency pairs must be ordered by stable type identity";
                }

                reason ??= ValidateFrom(compilation, source, destination);
                var entry = new MappingEntry(pair, assembly.Identity.ToString(), reason, dependencies.ToImmutable());
                if (entries.TryGetValue(pair, out var previous))
                {
                    // Identical repeated declarations are harmless; contradictory contracts are unavailable.
                    if (previous.InvalidReason != entry.InvalidReason ||
                        !previous.Dependencies.SequenceEqual(entry.Dependencies))
                        entries[pair] = new MappingEntry(pair, entry.ExternalAssembly, "conflicting metadata for the same pair");
                }
                else entries.Add(pair, entry);
            }
        }
        return entries;
    }

    public static bool RejectDispatchConflicts(Dictionary<MappingPair, MappingEntry> entries)
    {
        var conflicting = entries.Values.Where(entry => entry.ExternalAssembly is not null && entry.InvalidReason is null &&
            entry.SourceType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T } enumerable &&
            entries.Values.Any(other => other.InvalidReason is null &&
                SymbolEqualityComparer.Default.Equals(other.SourceType, enumerable.TypeArguments[0]))).ToArray();
        foreach (var entry in conflicting)
            entries[entry.Pair] = new MappingEntry(entry.Pair, entry.ExternalAssembly,
                "the source's MapTo signature conflicts with a generated collection overload for its element type", entry.Dependencies);
        return conflicting.Length > 0;
    }

    private static string? ValidateFrom(Compilation compilation, ITypeSymbol source, INamedTypeSymbol destination)
    {
        if (source is not INamedTypeSymbol namedSource || source.SpecialType == SpecialType.System_Object ||
            namedSource.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface) ||
            destination.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
            namedSource.IsStatic || namedSource.IsRefLikeType || destination.IsStatic || destination.IsRefLikeType ||
            !SymbolUtilities.IsPubliclyAccessible(source) || !SymbolUtilities.IsPubliclyAccessible(destination) ||
            SymbolUtilities.IsOpenType(source) || SymbolUtilities.IsOpenType(destination) ||
            !compilation.IsSymbolAccessibleWithin(source, compilation.Assembly) ||
            !compilation.IsSymbolAccessibleWithin(destination, compilation.Assembly))
            return "map types must be public, accessible, closed named types";

        var methods = destination.GetMembers("From").OfType<IMethodSymbol>().Where(method =>
            method is { IsStatic: true, MethodKind: MethodKind.Ordinary, Arity: 0, Parameters.Length: 1,
                ReturnsVoid: false, ReturnsByRef: false, ReturnsByRefReadonly: false, IsVararg: false, DeclaredAccessibility: Accessibility.Public } &&
            method.Parameters[0].RefKind == RefKind.None &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, source)).ToArray();
        if (methods.Length != 1 || !TransformResolver.CanAssign(compilation, methods[0].ReturnType, destination))
            return "expected one public static From(Source) with a return type assignable to the destination";
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        NullabilityPolicy.Validate(methods[0].ReturnType, destination.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
            null, "From", diagnostics);
        return diagnostics.Count == 0 ? null : "From return nullability does not satisfy the destination contract";
    }
}
