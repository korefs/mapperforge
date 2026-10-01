using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using MapperForge.Generator.Utilities;

namespace MapperForge.Generator.Models;

internal sealed class MappingRequest
{
    public MappingRequest(INamedTypeSymbol destinationType, ITypeSymbol sourceType, Location? location)
    {
        DestinationType = destinationType;
        SourceType = sourceType;
        Location = location;
    }

    public INamedTypeSymbol DestinationType { get; }

    public ITypeSymbol SourceType { get; }

    public Location? Location { get; }
}

internal sealed class MappingPlan
{
    public MappingPlan(
        INamedTypeSymbol destinationType,
        ITypeSymbol sourceType,
        ImmutableArray<MemberAssignment> assignments,
        ImmutableArray<Diagnostic> diagnostics,
        bool hidesInheritedFrom = false)
    {
        DestinationType = destinationType;
        SourceType = sourceType;
        Assignments = assignments;
        Diagnostics = diagnostics;
        HidesInheritedFrom = hidesInheritedFrom;
    }

    public INamedTypeSymbol DestinationType { get; }

    public ITypeSymbol SourceType { get; }

    public string MethodAccessibility => SymbolUtilities.IsPubliclyAccessible(SourceType) &&
        SymbolUtilities.IsPubliclyAccessible(DestinationType) ? "public" : "internal";

    public bool HidesInheritedFrom { get; }

    public ImmutableArray<MemberAssignment> Assignments { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

internal sealed class MemberAssignment
{
    public MemberAssignment(string destinationName, string expression)
    {
        DestinationName = destinationName;
        Expression = expression;
    }

    public string DestinationName { get; }

    public string Expression { get; }
}
