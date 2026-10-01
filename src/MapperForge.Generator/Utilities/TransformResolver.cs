using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MapperForge.Generator.Utilities;

internal static class TransformResolver
{
    public static IMethodSymbol? Find(Compilation compilation, INamedTypeSymbol destination, string name,
        ITypeSymbol sourceType, ITypeSymbol resultType)
    {
        var candidates = new List<IMethodSymbol>();
        var fallbackCandidates = new List<IMethodSymbol>();
        for (var current = destination; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(name).OfType<IMethodSymbol>())
            {
                if (!method.IsStatic || method.MethodKind != MethodKind.Ordinary || method.IsGenericMethod ||
                    method.ReturnsVoid || method.IsVararg ||
                    method.Parameters.Length != 1 || method.Parameters[0].RefKind != RefKind.None ||
                    !compilation.IsSymbolAccessibleWithin(method, destination)) continue;

                var directInput = CanAssign(compilation, sourceType, method.Parameters[0].Type);
                if (!directInput && !CanAssign(compilation, NullabilityPolicy.Unwrap(sourceType),
                    NullabilityPolicy.Unwrap(method.Parameters[0].Type))) continue;
                if (!CanAssign(compilation, method.ReturnType, resultType) &&
                    !CanAssign(compilation, NullabilityPolicy.Unwrap(method.ReturnType), NullabilityPolicy.Unwrap(resultType))) continue;
                // A derived declaration with the same input signature hides its ancestor.
                if (candidates.Concat(fallbackCandidates).Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, method.Parameters[0].Type))) continue;
                (directInput ? candidates : fallbackCandidates).Add(method);
            }
        }

        if (candidates.Count == 0) candidates = fallbackCandidates;
        var inputType = candidates == fallbackCandidates ? NullabilityPolicy.Unwrap(sourceType) : sourceType;
        var best = candidates.Where(candidate => candidates.All(other => SymbolEqualityComparer.Default.Equals(candidate, other) ||
            IsBetter(compilation, inputType, candidate.Parameters[0].Type, other.Parameters[0].Type))).ToArray();
        return best.Length == 1 ? best[0] : null;
    }

    private static bool IsBetter(Compilation compilation, ITypeSymbol source, ITypeSymbol left, ITypeSymbol right)
    {
        if (SymbolEqualityComparer.Default.Equals(source, left)) return !SymbolEqualityComparer.Default.Equals(source, right);
        if (SymbolEqualityComparer.Default.Equals(source, right)) return false;
        if (CanAssign(compilation, left, right) && !CanAssign(compilation, right, left)) return true;
        return (NullabilityPolicy.Unwrap(left).SpecialType, NullabilityPolicy.Unwrap(right).SpecialType) is
            (SpecialType.System_SByte, SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64) or
            (SpecialType.System_Int16, SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64) or
            (SpecialType.System_Int32, SpecialType.System_UInt32 or SpecialType.System_UInt64) or
            (SpecialType.System_Int64, SpecialType.System_UInt64);
    }

    public static bool CanAssign(Compilation compilation, ITypeSymbol source, ITypeSymbol destination)
    {
        if (SymbolEqualityComparer.Default.Equals(source, destination)) return true;
        return compilation is CSharpCompilation csharp && csharp.ClassifyConversion(source, destination) is { Exists: true, IsImplicit: true };
    }
}
