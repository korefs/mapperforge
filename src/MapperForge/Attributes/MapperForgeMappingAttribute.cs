namespace MapperForge;

/// <summary>Describes a generated public map for compile-time discovery by other assemblies.</summary>
/// <remarks>Version 1 dependencies alternate source and destination types for each direct map.</remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MapperForgeMappingAttribute : Attribute
{
    public MapperForgeMappingAttribute(Type sourceType, Type destinationType, int contractVersion, params Type[] dependencyPairs)
    {
        SourceType = sourceType;
        DestinationType = destinationType;
        ContractVersion = contractVersion;
        DependencyPairs = dependencyPairs;
    }

    public Type SourceType { get; }
    public Type DestinationType { get; }
    public int ContractVersion { get; }
    public Type[] DependencyPairs { get; }
}
