using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MapperForge.Generator.Diagnostics;
using MapperForge.Generator.Emitting;
using MapperForge.Generator.Models;
using MapperForge.Generator.Parsing;
using MapperForge.Generator.Utilities;

namespace MapperForge.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class MapperForgeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var requests = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MappingParser.MapFromAttributeMetadataName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, _) => MappingParser.Parse(syntaxContext))
            .SelectMany(static (items, _) => items);

        var compilationAndRequests = context.CompilationProvider.Combine(requests.Collect());

        context.RegisterSourceOutput(compilationAndRequests, static (sourceContext, pair) =>
        {
            var plans = BuildPlans(pair.Left, pair.Right);

            foreach (var plan in plans)
            {
                foreach (var diagnostic in plan.Diagnostics)
                {
                    sourceContext.ReportDiagnostic(diagnostic);
                }
            }

            var validPlans = plans
                .Where(static plan => !plan.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                .ToImmutableArray();

            foreach (var plan in validPlans)
            {
                sourceContext.AddSource(MappingEmitter.GetHintName(plan), SourceText.From(MappingEmitter.EmitDestinationMapping(plan), Encoding.UTF8));
            }

            if (validPlans.Length > 0)
            {
                sourceContext.AddSource("MapperForgeGeneratedExtensions.g.cs", SourceText.From(MappingEmitter.EmitExtensions(validPlans), Encoding.UTF8));
            }
        });
    }

    private static ImmutableArray<MappingPlan> BuildPlans(Compilation compilation, ImmutableArray<MappingRequest> requests)
    {
        var builder = ImmutableArray.CreateBuilder<MappingPlan>();
        var mappingIndex = requests
            .GroupBy(static request => new MappingPair(request.SourceType, request.DestinationType))
            .ToDictionary(static group => group.Key, static group => group.First());

        foreach (var request in mappingIndex.Values
            .OrderBy(static request => SymbolUtilities.GetStableTypeIdentity(request.DestinationType), System.StringComparer.Ordinal)
            .ThenBy(static request => SymbolUtilities.GetStableTypeIdentity(request.SourceType), System.StringComparer.Ordinal))
        {
            builder.Add(BuildPlan(compilation, request, mappingIndex));
        }

        return builder.ToImmutable();
    }

    private static MappingPlan BuildPlan(
        Compilation compilation,
        MappingRequest request,
        Dictionary<MappingPair, MappingRequest> mappingIndex)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var assignments = ImmutableArray.CreateBuilder<MemberAssignment>();

        if (!SymbolUtilities.IsPartial(request.DestinationType))
        {
            diagnostics.Add(Diagnostic.Create(
                MapperForgeDiagnostics.DestinationMustBePartial,
                request.Location ?? SymbolUtilities.GetLocation(request.DestinationType),
                request.DestinationType.ToDisplayString()));
        }

        foreach (var containingType in SymbolUtilities.GetContainingTypes(request.DestinationType))
        {
            if (!SymbolUtilities.IsPartial(containingType))
            {
                diagnostics.Add(Diagnostic.Create(
                    MapperForgeDiagnostics.DestinationMustBePartial,
                    SymbolUtilities.GetLocation(containingType),
                    containingType.ToDisplayString()));
            }
        }

        var sourceProperties = SymbolUtilities.GetProperties(request.SourceType, out var ambiguousSourceProperties)
            .Where(static property =>
                !property.IsStatic &&
                !property.IsIndexer &&
                property.DeclaredAccessibility == Accessibility.Public &&
                property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
            .ToDictionary(static property => property.Name);

        var destinationProperties = SymbolUtilities.GetProperties(request.DestinationType, out var ambiguousDestinationProperties)
            .Where(static property => !property.IsStatic && !property.IsIndexer && !property.IsImplicitlyDeclared)
            .ToArray();

        foreach (var ambiguity in ambiguousSourceProperties.Select(property => (Type: request.SourceType, Property: property))
            .Concat(ambiguousDestinationProperties.Select(property => (Type: request.DestinationType, Property: property))))
        {
            diagnostics.Add(Diagnostic.Create(
                MapperForgeDiagnostics.AmbiguousInheritedMember,
                request.Location ?? SymbolUtilities.GetLocation(ambiguity.Property),
                ambiguity.Type.ToDisplayString(), ambiguity.Property.Name));
        }

        foreach (var destinationProperty in destinationProperties)
        {
            if (HasAttribute(destinationProperty, "MapperForge.MapIgnoreAttribute"))
            {
                continue;
            }

            var propertyLocation = SymbolUtilities.GetLocation(destinationProperty);

            if (!HasAccessibleSetter(compilation, destinationProperty, request.DestinationType))
            {
                diagnostics.Add(Diagnostic.Create(
                    MapperForgeDiagnostics.DestinationSetterNotAccessible,
                    propertyLocation,
                    destinationProperty.Name));
                continue;
            }

            var sourceMemberName = GetStringAttributeArgument(destinationProperty, "MapperForge.MapPropertyAttribute")
                ?? destinationProperty.Name;

            if (!sourceProperties.TryGetValue(sourceMemberName, out var sourceProperty))
            {
                diagnostics.Add(Diagnostic.Create(
                    MapperForgeDiagnostics.DestinationMemberNotMapped,
                    propertyLocation,
                    destinationProperty.Name,
                    request.SourceType.ToDisplayString()));
                continue;
            }

            var sourceReceiver = request.SourceType.TypeKind == TypeKind.Interface &&
                !SymbolEqualityComparer.Default.Equals(sourceProperty.ContainingType, request.SourceType)
                ? "((" + sourceProperty.ContainingType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat) + ")source)"
                : "source";
            var sourceExpression = sourceReceiver + "." + SymbolUtilities.EscapeIdentifier(sourceProperty.Name);
            var transformMethodName = GetStringAttributeArgument(destinationProperty, "MapperForge.MapTransformAttribute");

            if (transformMethodName is not null)
            {
                var transformMethod = FindTransformMethod(compilation, request.DestinationType, transformMethodName, sourceProperty.Type, destinationProperty.Type);

                if (transformMethod is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        MapperForgeDiagnostics.TransformMethodInvalid,
                        propertyLocation,
                        transformMethodName));
                    continue;
                }

                sourceExpression = transformMethod.Name + "(" + sourceExpression + ")";
            }
            else if (CanAssign(compilation, sourceProperty.Type, destinationProperty.Type))
            {
                // Direct assignment is preferred when it is type-safe.
            }
            else if (TryCreateNestedExpression(sourceProperty.Type, destinationProperty.Type, sourceExpression, mappingIndex, out var nestedExpression))
            {
                sourceExpression = nestedExpression;
            }
            else if (TryCreateCollectionExpression(sourceProperty.Type, destinationProperty.Type, sourceExpression, mappingIndex, out var collectionExpression))
            {
                sourceExpression = collectionExpression;
            }
            else
            {
                diagnostics.Add(Diagnostic.Create(
                    MapperForgeDiagnostics.IncompatibleTypes,
                    propertyLocation,
                    sourceProperty.Name,
                    sourceProperty.Type.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat),
                    destinationProperty.Name,
                    destinationProperty.Type.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat)));
                continue;
            }

            assignments.Add(new MemberAssignment(destinationProperty.Name, sourceExpression));
        }

        return new MappingPlan(request.DestinationType, request.SourceType, assignments.ToImmutable(), diagnostics.ToImmutable());
    }

    private static bool TryCreateNestedExpression(
        ITypeSymbol sourceType,
        ITypeSymbol destinationType,
        string sourceExpression,
        Dictionary<MappingPair, MappingRequest> mappingIndex,
        out string expression)
    {
        expression = "";

        if (sourceType is not INamedTypeSymbol sourceNamedType ||
            destinationType is not INamedTypeSymbol destinationNamedType)
        {
            return false;
        }

        var mappingKey = new MappingPair(sourceNamedType, destinationNamedType);
        if (!mappingIndex.TryGetValue(mappingKey, out var nestedMapping))
        {
            return false;
        }

        var destinationTypeName = nestedMapping.DestinationType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat);
        var guardedSourceExpression = IsNullable(sourceType) ? sourceExpression + "!" : sourceExpression;
        var nestedCall = destinationTypeName + ".From(" + guardedSourceExpression + ")";

        expression = IsNullable(sourceType) || IsNullable(destinationType)
            ? sourceExpression + " is null ? null : " + nestedCall
            : nestedCall;

        return true;
    }

    private static bool TryCreateCollectionExpression(
        ITypeSymbol sourceType,
        ITypeSymbol destinationType,
        string sourceExpression,
        Dictionary<MappingPair, MappingRequest> mappingIndex,
        out string expression)
    {
        expression = "";

        var sourceElementType = TryGetEnumerableElementType(sourceType);
        var destinationElementType = TryGetSupportedDestinationCollectionElementType(destinationType);

        if (sourceElementType is not INamedTypeSymbol sourceElementNamedType ||
            destinationElementType is not INamedTypeSymbol destinationElementNamedType)
        {
            return false;
        }

        var mappingKey = new MappingPair(sourceElementNamedType, destinationElementNamedType);
        if (!mappingIndex.TryGetValue(mappingKey, out var nestedMapping))
        {
            return false;
        }

        var destinationElementTypeName = nestedMapping.DestinationType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat);
        var collectionCall = "global::MapperForge.MapperForgeGeneratedExtensions.MapToList<" +
            destinationElementTypeName +
            ">(" +
            (IsNullable(sourceType) ? sourceExpression + "!" : sourceExpression) +
            ")";

        expression = IsNullable(sourceType) || IsNullable(destinationType)
            ? sourceExpression + " is null ? null : " + collectionCall
            : collectionCall;

        return true;
    }

    private static ITypeSymbol? TryGetEnumerableElementType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType || namedType.SpecialType == SpecialType.System_String)
        {
            return null;
        }

        if (namedType.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return namedType.TypeArguments[0];
        }

        foreach (var interfaceType in namedType.AllInterfaces)
        {
            if (interfaceType.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return interfaceType.TypeArguments[0];
            }
        }

        return null;
    }

    private static ITypeSymbol? TryGetSupportedDestinationCollectionElementType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return null;
        }

        var metadataName = namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        return metadataName is "System.Collections.Generic.List<T>" or "System.Collections.Generic.IReadOnlyList<T>"
            ? namedType.TypeArguments[0]
            : null;
    }

    private static bool IsNullable(ITypeSymbol type)
    {
        return type.NullableAnnotation == NullableAnnotation.Annotated;
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);
    }

    private static string? GetStringAttributeArgument(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == metadataName &&
                attribute.ConstructorArguments.Length == 1)
            {
                return attribute.ConstructorArguments[0].Value as string;
            }
        }

        return null;
    }

    private static bool HasAccessibleSetter(Compilation compilation, IPropertySymbol property, INamedTypeSymbol destinationType)
    {
        var setMethod = property.SetMethod;
        if (setMethod is null)
        {
            return false;
        }

        return compilation.IsSymbolAccessibleWithin(setMethod, destinationType, destinationType);
    }

    private static IMethodSymbol? FindTransformMethod(
        Compilation compilation,
        INamedTypeSymbol destinationType,
        string methodName,
        ITypeSymbol sourceType,
        ITypeSymbol destinationMemberType)
    {
        for (var current = destinationType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (!member.IsStatic || member.Parameters.Length != 1 ||
                    !compilation.IsSymbolAccessibleWithin(member, destinationType))
                {
                    continue;
                }

                if (!CanAssign(compilation, sourceType, member.Parameters[0].Type) ||
                    !CanAssign(compilation, member.ReturnType, destinationMemberType))
                {
                    continue;
                }

                return member;
            }
        }

        return null;
    }

    private static bool CanAssign(Compilation compilation, ITypeSymbol sourceType, ITypeSymbol destinationType)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, destinationType))
        {
            return true;
        }

        if (compilation is not CSharpCompilation csharpCompilation)
        {
            return false;
        }

        var conversion = csharpCompilation.ClassifyConversion(sourceType, destinationType);
        return conversion.Exists && conversion.IsImplicit;
    }
}
