using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MapperForge.Generator.Diagnostics;
using MapperForge.Generator.Models;
using MapperForge.Generator.Emitting;

namespace MapperForge.Generator.Utilities;

internal static class MappingValidation
{
    public static bool ValidateTypes(Compilation compilation, MappingRequest request, IEnumerable<ITypeSymbol> declaredSources,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var destination = request.DestinationType;
        var reason = GetUnsupportedReason(compilation, request.SourceType, isDestination: false) ??
            GetUnsupportedReason(compilation, destination, isDestination: true);
        if (reason is null && request.SourceType is INamedTypeSymbol
            { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T } enumerable &&
            declaredSources.Any(source => SymbolEqualityComparer.Default.Equals(source, enumerable.TypeArguments[0])))
        {
            reason = "the source's MapTo signature conflicts with a generated collection overload for its element type";
        }

        foreach (var container in SymbolUtilities.GetContainingTypes(destination))
        {
            if (reason is null && (container.TypeParameters.Length > 0 || container.IsRefLikeType ||
                container.TypeKind is not (TypeKind.Class or TypeKind.Struct)))
            {
                reason = "destination containers must be non-generic classes, structs or records without ref-like semantics";
            }
        }

        if (reason is null && destination.GetMembers("From").Any(member => member is not IMethodSymbol ||
            member is IMethodSymbol { Arity: 0, Parameters.Length: 1 } method &&
            method.Parameters[0].RefKind == RefKind.None &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, request.SourceType)))
        {
            reason = "an existing member conflicts with the generated From signature";
        }

        var helper = compilation.GetTypeByMetadataName("MapperForge.MapperForgeGeneratedExtensions") ??
            compilation.GetTypeByMetadataName("MapperForge." + MappingEmitter.GetHelperName(compilation));
        if (reason is null && helper is not null && SymbolEqualityComparer.Default.Equals(helper.ContainingAssembly, compilation.Assembly))
        {
            reason = "MapperForge.MapperForgeGeneratedExtensions is reserved for generated methods";
        }

        if (reason is null) return true;
        diagnostics.Add(Diagnostic.Create(MapperForgeDiagnostics.UnsupportedMapping,
            request.Location ?? SymbolUtilities.GetLocation(destination),
            request.SourceType.ToDisplayString(), destination.ToDisplayString(), reason));
        return false;
    }

    private static string? GetUnsupportedReason(Compilation compilation, ITypeSymbol type, bool isDestination)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface) ||
            named.IsStatic || named.IsRefLikeType || named.IsFileLocal)
        {
            return "type must be a non-static, non-ref-like named class, struct, record or source interface";
        }

        if (SymbolUtilities.IsOpenType(type)) return "open generic types are not supported";
        if (!isDestination && named.SpecialType == SpecialType.System_Object)
            return "object-typed sources use the runtime failure fallback and cannot declare generated maps";
        if (isDestination && (named.TypeKind == TypeKind.Interface || named.IsAbstract || named.TypeParameters.Length > 0))
        {
            return "destination must be a concrete non-generic class, struct or record";
        }

        if (!IsAccessibleFromHelper(compilation, type)) return "type is inaccessible to the generated mapping helper";
        return null;
    }

    private static bool IsAccessibleFromHelper(Compilation compilation, ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsAccessibleFromHelper(compilation, array.ElementType),
        INamedTypeSymbol named => !named.IsFileLocal && compilation.IsSymbolAccessibleWithin(named, compilation.Assembly) &&
            named.TypeArguments.All(argument => IsAccessibleFromHelper(compilation, argument)) &&
            (named.ContainingType is null || IsAccessibleFromHelper(compilation, named.ContainingType)),
        _ => false
    };

    public static IMethodSymbol? FindConstructor(Compilation compilation, MappingRequest request,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var declarations = request.DestinationType.DeclaringSyntaxReferences
            .Select(static reference => (TypeDeclarationSyntax)reference.GetSyntax()).ToArray();
        var declaration = declarations.FirstOrDefault(static syntax => syntax.Members.OfType<ConstructorDeclarationSyntax>()
            .Any(static constructor => constructor.Body is not null || constructor.ExpressionBody is not null)) ?? declarations[0];
        var expression = SyntaxFactory.ParseExpression("new " + request.DestinationType.ToDisplayString(SymbolUtilities.FullyQualifiedNullableFormat) + "()");
        var position = declaration.CloseBraceToken.IsMissing || declaration.CloseBraceToken.RawKind == 0
            ? declaration.Identifier.Span.End : declaration.CloseBraceToken.SpanStart;
        var constructorSyntax = declaration.Members.OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(static constructor => constructor.Body is not null || constructor.ExpressionBody is not null);
        if (constructorSyntax is not null)
            position = constructorSyntax.Body?.OpenBraceToken.Span.End ?? constructorSyntax.ExpressionBody!.Expression.SpanStart;
        var info = compilation.GetSemanticModel(declaration.SyntaxTree)
            .GetSpeculativeSymbolInfo(position, expression, SpeculativeBindingOption.BindAsExpression);
        if (info.Symbol is IMethodSymbol constructor) return constructor;
        if (info.CandidateSymbols.Length == 1 && info.CandidateSymbols[0] is IMethodSymbol candidate &&
            candidate.Parameters.All(static parameter => parameter.IsOptional || parameter.IsParams) &&
            compilation.IsSymbolAccessibleWithin(candidate, request.DestinationType)) return candidate;

        diagnostics.Add(Diagnostic.Create(MapperForgeDiagnostics.InvalidConstruction,
            request.Location ?? SymbolUtilities.GetLocation(request.DestinationType),
            request.DestinationType.ToDisplayString(), "no accessible, unambiguous constructor can be invoked without arguments"));
        return null;
    }

    public static void ValidateRequired(MappingRequest request, IMethodSymbol? constructor,
        ImmutableArray<MemberAssignment> assignments, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (constructor is null || constructor.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute")) return;

        for (var type = request.DestinationType; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers().Where(static member =>
                member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                if (member is IPropertySymbol && assignments.Any(assignment => assignment.DestinationName == member.Name)) continue;
                diagnostics.Add(Diagnostic.Create(MapperForgeDiagnostics.InvalidConstruction,
                    SymbolUtilities.GetLocation(member) ?? request.Location,
                    request.DestinationType.ToDisplayString(), "required member '" + member.Name + "' is not initialized"));
            }
        }
    }
}
