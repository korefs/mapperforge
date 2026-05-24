using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Models;

internal sealed class MappingRequest
{
    public MappingRequest(INamedTypeSymbol destinationType, INamedTypeSymbol sourceType, Location? location)
    {
        DestinationType = destinationType;
        SourceType = sourceType;
        Location = location;
    }

    public INamedTypeSymbol DestinationType { get; }

    public INamedTypeSymbol SourceType { get; }

    public Location? Location { get; }
}

internal sealed class MappingPlan
{
    public MappingPlan(
        INamedTypeSymbol destinationType,
        INamedTypeSymbol sourceType,
        ImmutableArray<MemberAssignment> assignments,
        ImmutableArray<Diagnostic> diagnostics)
    {
        DestinationType = destinationType;
        SourceType = sourceType;
        Assignments = assignments;
        Diagnostics = diagnostics;
    }

    public INamedTypeSymbol DestinationType { get; }

    public INamedTypeSymbol SourceType { get; }

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
