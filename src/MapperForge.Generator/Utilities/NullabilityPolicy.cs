using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using MapperForge.Generator.Diagnostics;

namespace MapperForge.Generator.Utilities;

internal static class NullabilityPolicy
{
    public static bool IsNullable(ITypeSymbol type) => type.NullableAnnotation == NullableAnnotation.Annotated ||
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    public static bool IsNonNullable(ITypeSymbol type) => type.IsValueType && !IsNullable(type) ||
        type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.NotAnnotated;

    public static bool IsUnknown(ITypeSymbol type) => type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.None;

    public static ITypeSymbol Unwrap(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0] : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

    public static bool Validate(ITypeSymbol source, ITypeSymbol destination, Location? location, string member,
        ImmutableArray<Diagnostic>.Builder diagnostics, bool checkTypeArguments = true, bool checkRoot = true)
    {
        var valid = true;
        var warned = false;
        Compare(source, destination, checkRoot);
        return valid;

        void Compare(ITypeSymbol from, ITypeSymbol to, bool compareHere = true)
        {
            if (compareHere && IsNullable(from) && IsNonNullable(to))
            {
                if (valid) diagnostics.Add(Diagnostic.Create(MapperForgeDiagnostics.NullableToNonNullable,
                    location, from.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat),
                    to.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat), member));
                valid = false;
            }
            else if (compareHere && IsUnknown(from) && IsNonNullable(to) && !warned)
            {
                diagnostics.Add(Diagnostic.Create(MapperForgeDiagnostics.UnknownNullability,
                    location, from.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat),
                    to.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat), member));
                warned = true;
            }

            if (!checkTypeArguments) return;
            from = Unwrap(from);
            to = Unwrap(to);
            if (from is IArrayTypeSymbol sourceArray && to is IArrayTypeSymbol destinationArray)
            {
                Compare(sourceArray.ElementType, destinationArray.ElementType);
            }
            else if (from is INamedTypeSymbol sourceNamed && to is INamedTypeSymbol destinationNamed)
            {
                var alignedSource = FindConstructedType(sourceNamed, destinationNamed.OriginalDefinition);
                if (alignedSource is null) return;
                for (var index = 0; index < alignedSource.TypeArguments.Length; index++)
                {
                    if (destinationNamed.TypeParameters[index].Variance == VarianceKind.In)
                        Compare(destinationNamed.TypeArguments[index], alignedSource.TypeArguments[index]);
                    else
                        Compare(alignedSource.TypeArguments[index], destinationNamed.TypeArguments[index]);
                }
                if (alignedSource.ContainingType is not null && destinationNamed.ContainingType is not null)
                    Compare(alignedSource.ContainingType, destinationNamed.ContainingType, compareHere: false);
            }
        }
    }

    private static INamedTypeSymbol? FindConstructedType(INamedTypeSymbol source, INamedTypeSymbol definition)
    {
        for (var type = source; type is not null; type = type.BaseType)
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition)) return type;
        return source.AllInterfaces.FirstOrDefault(type => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition));
    }
}
