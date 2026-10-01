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
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { } declaration &&
                    (declaration.AttributeLists.Count > 0 || declaration.BaseList is not null),
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
        var mappingIndex = requests
            .GroupBy(static request => new MappingPair(request.SourceType, request.DestinationType))
            .ToDictionary(static group => group.Key, static group => group.First());

        var distinctRequests = mappingIndex.Values
            .OrderBy(static request => SymbolUtilities.GetStableTypeIdentity(request.DestinationType), System.StringComparer.Ordinal)
            .ThenBy(static request => SymbolUtilities.GetStableTypeIdentity(request.SourceType), System.StringComparer.Ordinal).ToArray();
        var declaredSources = distinctRequests.Select(static request => request.SourceType).ToArray();

        while (true)
        {
            var plans = distinctRequests.Select(request => BuildPlan(compilation, request, mappingIndex, declaredSources)).ToImmutableArray();
            var invalidPairs = plans.Where(static plan => plan.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                .Select(static plan => new MappingPair(plan.SourceType, plan.DestinationType)).ToArray();
            var removed = false;
            foreach (var invalid in invalidPairs) removed |= mappingIndex.Remove(invalid);
            // Do not emit a call to a local From overload that was rejected. Detailed dependency
            // diagnostics and recursive-graph validation are implemented in the dependency stage.
            if (!removed)
            {
                return plans.Select(plan => new MappingPlan(plan.DestinationType, plan.SourceType, plan.Assignments, plan.Diagnostics,
                    HasInheritedFrom(compilation, plan, plans))).ToImmutableArray();
            }
        }
    }

    private static bool HasInheritedFrom(Compilation compilation, MappingPlan plan, ImmutableArray<MappingPlan> plans)
    {
        for (var type = plan.DestinationType.BaseType; type is not null; type = type.BaseType)
        {
            if (type.GetMembers("From").Any(member => compilation.IsSymbolAccessibleWithin(member, plan.DestinationType) &&
                (member is not IMethodSymbol || member is IMethodSymbol { Arity: 0, Parameters.Length: 1 } method &&
                    method.Parameters[0].RefKind == RefKind.None &&
                    SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, plan.SourceType)))) return true;
            if (plans.Any(other => SymbolEqualityComparer.Default.Equals(other.DestinationType, type) &&
                SymbolEqualityComparer.Default.Equals(other.SourceType, plan.SourceType) &&
                !other.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))) return true;
        }
        return false;
    }

    private static MappingPlan BuildPlan(
        Compilation compilation,
        MappingRequest request,
        Dictionary<MappingPair, MappingRequest> mappingIndex,
        IEnumerable<ITypeSymbol> declaredSources)
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

        if (!MappingValidation.ValidateTypes(compilation, request, declaredSources, diagnostics))
            return new MappingPlan(request.DestinationType, request.SourceType, assignments.ToImmutable(), diagnostics.ToImmutable());
        var constructor = MappingValidation.FindConstructor(compilation, request, diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new MappingPlan(request.DestinationType, request.SourceType, assignments.ToImmutable(), diagnostics.ToImmutable());

        var sourceType = (INamedTypeSymbol)request.SourceType;
        var sourceProperties = SymbolUtilities.GetProperties(sourceType, out var ambiguousSourceProperties)
            .Where(static property =>
                !property.IsStatic &&
                !property.IsIndexer &&
                property.DeclaredAccessibility == Accessibility.Public &&
                property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
            .ToDictionary(static property => property.Name);

        var destinationProperties = SymbolUtilities.GetProperties(request.DestinationType, out var ambiguousDestinationProperties)
            .Where(static property => !property.IsStatic && !property.IsIndexer && !property.IsImplicitlyDeclared)
            .ToArray();

        foreach (var ambiguity in ambiguousSourceProperties.Select(property => (Type: sourceType, Property: property))
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
            var valueName = "__mfgValue" + assignments.Count;

            if (sourceProperty.RefKind != RefKind.None || destinationProperty.RefKind != RefKind.None ||
                sourceProperty.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
                destinationProperty.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
            {
                diagnostics.Add(Diagnostic.Create(MapperForgeDiagnostics.UnsupportedMapping, propertyLocation,
                    request.SourceType.ToDisplayString(), request.DestinationType.ToDisplayString(),
                    "member '" + destinationProperty.Name + "' has an unsupported ref or pointer signature"));
                continue;
            }

            if (transformMethodName is not null)
            {
                var canLift = NullabilityPolicy.IsNullable(destinationProperty.Type) &&
                    (NullabilityPolicy.IsNullable(sourceProperty.Type) || NullabilityPolicy.IsUnknown(sourceProperty.Type));
                var transformMethod = TransformResolver.Find(compilation, request.DestinationType, transformMethodName,
                    sourceProperty.Type, destinationProperty.Type);

                if (transformMethod is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        MapperForgeDiagnostics.TransformMethodInvalid,
                        propertyLocation,
                        transformMethodName,
                        sourceProperty.Type.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat),
                        destinationProperty.Type.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat)));
                    continue;
                }

                var parameterType = transformMethod.Parameters[0].Type;
                var lift = canLift && NullabilityPolicy.IsNonNullable(parameterType);
                if (NullabilityPolicy.IsNullable(sourceProperty.Type) && NullabilityPolicy.IsNonNullable(destinationProperty.Type) &&
                    (!NullabilityPolicy.IsNullable(parameterType) || !NullabilityPolicy.IsNonNullable(transformMethod.ReturnType)))
                {
                    diagnostics.Add(Diagnostic.Create(MapperForgeDiagnostics.NullableToNonNullable, propertyLocation,
                        sourceProperty.Type.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat),
                        destinationProperty.Type.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat), destinationProperty.Name));
                    continue;
                }
                if (!NullabilityPolicy.Validate(lift ? NullabilityPolicy.Unwrap(sourceProperty.Type) : sourceProperty.Type,
                        parameterType, propertyLocation, destinationProperty.Name, diagnostics) ||
                    !NullabilityPolicy.Validate(transformMethod.ReturnType, destinationProperty.Type,
                        propertyLocation, destinationProperty.Name, diagnostics)) continue;

                var receiver = transformMethod.ContainingType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat);
                var input = lift ? valueName : sourceExpression;
                var call = receiver + "." + SymbolUtilities.EscapeIdentifier(transformMethod.Name) + "((" +
                    parameterType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat) + ")" + input + ")";
                sourceExpression = lift ? sourceExpression + " is { } " + valueName + " ? " + call + " : null" : call;
            }
            else
            {
                if (!NullabilityPolicy.Validate(sourceProperty.Type, destinationProperty.Type,
                    propertyLocation, destinationProperty.Name, diagnostics, checkTypeArguments: false)) continue;
                if (CanAssign(compilation, sourceProperty.Type, destinationProperty.Type))
                {
                    if (!NullabilityPolicy.Validate(sourceProperty.Type, destinationProperty.Type,
                        propertyLocation, destinationProperty.Name, diagnostics, checkRoot: false)) continue;
                    if (destinationProperty.Type is INamedTypeSymbol { } namedDestination &&
                        (namedDestination.Arity > 0 || namedDestination.ContainingType is not null) &&
                        (!namedDestination.IsReferenceType || namedDestination.NullableAnnotation != NullableAnnotation.None) &&
                        SymbolEqualityComparer.Default.Equals(sourceProperty.Type, destinationProperty.Type) &&
                        !SymbolEqualityComparer.IncludeNullability.Equals(sourceProperty.Type, destinationProperty.Type))
                    {
                        // CLR-identical generic types can safely widen annotations after null-policy
                        // validation. Spell the annotation conversion explicitly to avoid CS8619.
                        var objectType = NullabilityPolicy.IsNullable(destinationProperty.Type) ? "object?" : "object";
                        sourceExpression = "(" + destinationProperty.Type.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat) +
                            ")(" + objectType + ")" + sourceExpression;
                    }
                }
                else if (TryCreateNestedExpression(sourceProperty.Type, destinationProperty.Type, sourceExpression,
                    valueName, mappingIndex, out var nestedExpression))
                {
                    sourceExpression = nestedExpression;
                }
                else if (TryCreateCollectionExpression(sourceProperty.Type, destinationProperty.Type, sourceExpression,
                    valueName, mappingIndex, propertyLocation, destinationProperty.Name, diagnostics, out var collectionExpression))
                {
                    if (collectionExpression.Length == 0) continue;
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
            }

            assignments.Add(new MemberAssignment(destinationProperty.Name, sourceExpression));
        }

        var mappedAssignments = assignments.ToImmutable();
        MappingValidation.ValidateRequired(request, constructor, mappedAssignments, diagnostics);
        return new MappingPlan(request.DestinationType, request.SourceType, mappedAssignments, diagnostics.ToImmutable());
    }

    private static bool TryCreateNestedExpression(
        ITypeSymbol sourceType,
        ITypeSymbol destinationType,
        string sourceExpression,
        string valueName,
        Dictionary<MappingPair, MappingRequest> mappingIndex,
        out string expression)
    {
        expression = "";

        if (NullabilityPolicy.Unwrap(sourceType) is not INamedTypeSymbol sourceNamedType ||
            NullabilityPolicy.Unwrap(destinationType) is not INamedTypeSymbol destinationNamedType)
        {
            return false;
        }

        var mappingKey = new MappingPair(sourceNamedType, destinationNamedType);
        if (!mappingIndex.TryGetValue(mappingKey, out var nestedMapping))
        {
            return false;
        }

        var destinationTypeName = nestedMapping.DestinationType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat);
        var guard = NullabilityPolicy.IsNullable(sourceType) ||
            NullabilityPolicy.IsUnknown(sourceType) && NullabilityPolicy.IsNullable(destinationType);
        var nestedCall = destinationTypeName + ".From(" + (guard ? valueName : sourceExpression) + ")";
        expression = guard ? sourceExpression + " is { } " + valueName + " ? " + nestedCall + " : null" : nestedCall;

        return true;
    }

    private static bool TryCreateCollectionExpression(
        ITypeSymbol sourceType,
        ITypeSymbol destinationType,
        string sourceExpression,
        string valueName,
        Dictionary<MappingPair, MappingRequest> mappingIndex,
        Location? location,
        string member,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        out string expression)
    {
        expression = "";

        var sourceElementType = TryGetEnumerableElementType(sourceType);
        var destinationElementType = TryGetSupportedDestinationCollectionElementType(destinationType);

        if (sourceElementType is null || destinationElementType is null ||
            NullabilityPolicy.Unwrap(sourceElementType) is not INamedTypeSymbol sourceElementNamedType ||
            NullabilityPolicy.Unwrap(destinationElementType) is not INamedTypeSymbol destinationElementNamedType)
        {
            return false;
        }

        var mappingKey = new MappingPair(sourceElementNamedType, destinationElementNamedType);
        if (!mappingIndex.TryGetValue(mappingKey, out var nestedMapping))
        {
            return false;
        }

        if (!NullabilityPolicy.Validate(sourceElementType, destinationElementType, location, member, diagnostics)) return true;

        var destinationElementTypeName = nestedMapping.DestinationType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat);
        var guard = NullabilityPolicy.IsNullable(sourceType) ||
            NullabilityPolicy.IsUnknown(sourceType) && NullabilityPolicy.IsNullable(destinationType);
        var collectionSource = guard ? valueName : sourceExpression;
        string collectionCall;
        if (NullabilityPolicy.IsNullable(sourceElementType) || NullabilityPolicy.IsNullable(destinationElementType))
        {
            var elementGuard = NullabilityPolicy.IsNullable(sourceElementType) || NullabilityPolicy.IsUnknown(sourceElementType);
            var mapped = "(" + destinationElementType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat) + ")" +
                destinationElementTypeName + ".From(" + (elementGuard ? "value" : "item") + ")";
            var element = elementGuard ? "item is { } value ? " + mapped + " : null" : mapped;
            collectionCall = "global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(" +
                collectionSource + ", static item => " + element + "))";
        }
        else
        {
            collectionCall = "global::MapperForge.MapperForgeGeneratedExtensions.MapToList<" +
                destinationElementTypeName + ">(" + collectionSource + ")";
        }
        expression = guard ? sourceExpression + " is { } " + valueName + " ? " + collectionCall + " : null" : collectionCall;

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

    private static bool CanAssign(Compilation compilation, ITypeSymbol sourceType, ITypeSymbol destinationType)
        => TransformResolver.CanAssign(compilation, sourceType, destinationType);
}
