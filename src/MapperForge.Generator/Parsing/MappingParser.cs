using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using MapperForge.Generator.Models;

namespace MapperForge.Generator.Parsing;

internal static class MappingParser
{
    public const string MapFromAttributeMetadataName = "MapperForge.MapFromAttribute";

    public static ImmutableArray<MappingRequest> Parse(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol destinationType)
        {
            return ImmutableArray<MappingRequest>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<MappingRequest>();

        foreach (var attribute in context.Attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() != MapFromAttributeMetadataName)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is INamedTypeSymbol sourceType)
            {
                builder.Add(new MappingRequest(destinationType, sourceType, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
            }
        }

        return builder.ToImmutable();
    }
}
