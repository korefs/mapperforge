using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MapperForge.Generator.Utilities;
using MapperForge.Generator.Models;

namespace MapperForge.Generator.Parsing;

internal static class MappingParser
{
    public const string MapFromAttributeMetadataName = "MapperForge.MapFromAttribute";

    public static ImmutableArray<MappingRequest> Parse(GeneratorSyntaxContext context)
    {
        if (context.SemanticModel.GetDeclaredSymbol((TypeDeclarationSyntax)context.Node) is not INamedTypeSymbol destinationType)
        {
            return ImmutableArray<MappingRequest>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<MappingRequest>();

        foreach (var attribute in destinationType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != MapFromAttributeMetadataName)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is ITypeSymbol sourceType)
            {
                builder.Add(new MappingRequest(destinationType, sourceType, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
            }
        }

        // Interfaces can compose marker contracts for concrete destinations; they are not maps themselves.
        if (destinationType.TypeKind == TypeKind.Interface) return builder.ToImmutable();

        foreach (var interfaceType in destinationType.AllInterfaces)
        {
            if (interfaceType.OriginalDefinition.MetadataName == "IMapFrom`1" &&
                interfaceType.ContainingNamespace.ToDisplayString() == "MapperForge")
            {
                builder.Add(new MappingRequest(destinationType, interfaceType.TypeArguments[0], SymbolUtilities.GetLocation(destinationType)));
            }
        }

        return builder.ToImmutable();
    }
}
