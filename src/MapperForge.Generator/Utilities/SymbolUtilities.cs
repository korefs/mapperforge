using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MapperForge.Generator.Utilities;

internal static class SymbolUtilities
{
    public static readonly SymbolDisplayFormat FullyQualifiedNullableFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    public static Location? GetLocation(ISymbol symbol)
    {
        return symbol.Locations.FirstOrDefault(static location => location.IsInSource);
    }

    public static string GetTypeKeyword(INamedTypeSymbol type)
    {
        var declaration = type.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (declaration is RecordDeclarationSyntax recordDeclaration)
        {
            return recordDeclaration.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record";
        }

        return type.TypeKind == TypeKind.Struct ? "struct" : "class";
    }

    public static IEnumerable<INamedTypeSymbol> GetContainingTypes(INamedTypeSymbol type)
    {
        var stack = new Stack<INamedTypeSymbol>();
        var current = type.ContainingType;

        while (current is not null)
        {
            stack.Push(current);
            current = current.ContainingType;
        }

        return stack;
    }

    public static string EscapeIdentifier(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;
    }

    public static ImmutableArray<IPropertySymbol> GetProperties(
        INamedTypeSymbol type, out ImmutableArray<IPropertySymbol> ambiguousProperties)
    {
        var properties = new List<IPropertySymbol>();
        var ambiguous = ImmutableArray.CreateBuilder<IPropertySymbol>();

        if (type.TypeKind != TypeKind.Interface)
        {
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            for (var current = type; current is not null; current = current.BaseType)
            {
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (!property.IsIndexer && !property.IsImplicitlyDeclared && names.Add(property.Name))
                    {
                        properties.Add(property);
                    }
                }
            }
        }
        else
        {
            var candidates = new[] { type }.Concat(type.AllInterfaces)
                .SelectMany(static current => current.GetMembers().OfType<IPropertySymbol>())
                .Where(static property => !property.IsIndexer && !property.IsImplicitlyDeclared);

            foreach (var group in candidates.GroupBy(static property => property.Name))
            {
                // A declaration in a derived interface wins over all its ancestors.
                var prevailing = group.Where(property => !group.Any(other =>
                        other.ContainingType.AllInterfaces.Contains(property.ContainingType, SymbolEqualityComparer.Default)))
                    .OrderBy(static property => property.ContainingType.ToDisplayString(), System.StringComparer.Ordinal)
                    .ToArray();
                var selected = prevailing[0];
                if (prevailing.Any(property => !HaveCompatibleSignatures(selected, property)))
                {
                    ambiguous.Add(selected);
                }
                else
                {
                    properties.Add(selected);
                }
            }
        }

        ambiguousProperties = ambiguous.OrderBy(static property => property.Name, System.StringComparer.Ordinal).ToImmutableArray();
        return properties.OrderBy(static property => property.Name, System.StringComparer.Ordinal).ToImmutableArray();
    }

    private static bool HaveCompatibleSignatures(IPropertySymbol left, IPropertySymbol right) =>
        SymbolEqualityComparer.Default.Equals(left.Type, right.Type) &&
        left.RefKind == right.RefKind && left.IsStatic == right.IsStatic &&
        (left.GetMethod is null) == (right.GetMethod is null) &&
        (left.SetMethod is null) == (right.SetMethod is null) &&
        left.SetMethod?.IsInitOnly == right.SetMethod?.IsInitOnly;

    public static string GetStableTypeIdentity(ITypeSymbol type)
    {
        var identity = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "@" + type.ContainingAssembly?.Identity;
        if (type is INamedTypeSymbol namedType)
        {
            identity += "[" + string.Join(";", namedType.TypeArguments.Select(GetStableTypeIdentity)) + "]";
            if (namedType.ContainingType is not null)
            {
                identity += "/" + GetStableTypeIdentity(namedType.ContainingType);
            }
        }
        else if (type is IArrayTypeSymbol arrayType)
        {
            identity += "[" + GetStableTypeIdentity(arrayType.ElementType) + "]";
        }

        return identity;
    }
}
