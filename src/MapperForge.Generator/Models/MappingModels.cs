using System.Collections.Immutable;
using System.Linq;
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
        bool hidesInheritedFrom = false,
        ImmutableArray<MappingPair> dependencies = default,
        ConstructionPlan? construction = null)
    {
        Construction = construction;
        DestinationType = destinationType;
        SourceType = sourceType;
        Assignments = assignments;
        Diagnostics = diagnostics;
        HidesInheritedFrom = hidesInheritedFrom;
        Dependencies = dependencies.IsDefault ? ImmutableArray<MappingPair>.Empty : dependencies;
    }

    public INamedTypeSymbol DestinationType { get; }

    public ITypeSymbol SourceType { get; }

    public string MethodAccessibility => SymbolUtilities.IsPubliclyAccessible(SourceType) &&
        SymbolUtilities.IsPubliclyAccessible(DestinationType) ? "public" : "internal";

    public bool HidesInheritedFrom { get; }

    public ConstructionPlan? Construction { get; }

    public ImmutableArray<MemberAssignment> Assignments { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public ImmutableArray<MappingPair> Dependencies { get; }
}

internal sealed class MappingEntry
{
    public MappingEntry(MappingPair pair, string? externalAssembly = null, string? invalidReason = null,
        ImmutableArray<MappingPair> dependencies = default)
    {
        Pair = pair;
        ExternalAssembly = externalAssembly;
        InvalidReason = invalidReason;
        Dependencies = dependencies.IsDefault ? ImmutableArray<MappingPair>.Empty : dependencies;
    }

    public MappingPair Pair { get; }
    public ITypeSymbol SourceType => Pair.Source;
    public INamedTypeSymbol DestinationType => Pair.Destination;
    public string? ExternalAssembly { get; }
    public string? InvalidReason { get; }
    public ImmutableArray<MappingPair> Dependencies { get; }
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

internal sealed class ConstructionPlan
{
    public ConstructionPlan(IMethodSymbol constructor, ImmutableArray<ConstructorArgument> arguments,
        ImmutableArray<IPropertySymbol> satisfiedMembers)
    {
        Constructor = constructor;
        Arguments = arguments;
        SatisfiedMembers = satisfiedMembers;
    }

    public IMethodSymbol Constructor { get; }
    public ImmutableArray<ConstructorArgument> Arguments { get; }
    public ImmutableArray<IPropertySymbol> SatisfiedMembers { get; }
    public int RequiredArguments => Arguments.Count(argument => !argument.Parameter.IsOptional && !argument.Parameter.IsParams);
}

internal sealed class ConstructorArgument
{
    public ConstructorArgument(IParameterSymbol parameter, string expression)
    {
        Parameter = parameter;
        Expression = expression;
    }

    public IParameterSymbol Parameter { get; }
    public string Expression { get; }
}
