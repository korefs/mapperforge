using System;
using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Models;

internal readonly struct MappingPair : IEquatable<MappingPair>
{
    public MappingPair(INamedTypeSymbol source, INamedTypeSymbol destination)
    {
        Source = source;
        Destination = destination;
    }

    private INamedTypeSymbol Source { get; }
    private INamedTypeSymbol Destination { get; }

    public bool Equals(MappingPair other) =>
        SymbolEqualityComparer.Default.Equals(Source, other.Source) &&
        SymbolEqualityComparer.Default.Equals(Destination, other.Destination);

    public override bool Equals(object? obj) => obj is MappingPair other && Equals(other);

    public override int GetHashCode() => unchecked(
        SymbolEqualityComparer.Default.GetHashCode(Source) * 397 ^
        SymbolEqualityComparer.Default.GetHashCode(Destination));
}
