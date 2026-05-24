namespace MapperForge;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MapTransformAttribute(string methodName) : Attribute
{
    public string MethodName { get; } = methodName;
}
