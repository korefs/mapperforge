namespace MapperForge;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MapPropertyAttribute(string sourceMemberName) : Attribute
{
    public string SourceMemberName { get; } = sourceMemberName;
}
