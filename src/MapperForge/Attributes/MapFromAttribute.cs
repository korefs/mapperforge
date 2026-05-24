namespace MapperForge;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class MapFromAttribute(Type sourceType) : Attribute
{
    public Type SourceType { get; } = sourceType;
}
