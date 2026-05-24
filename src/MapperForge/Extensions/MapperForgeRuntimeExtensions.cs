namespace MapperForge;

public static class MapperForgeRuntimeExtensions
{
    public static TDestination MapTo<TDestination>(this object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        throw new MapperForgeMappingException(
            $"MapperForge could not find a generated mapping from '{source.GetType().FullName}' to '{typeof(TDestination).FullName}'.");
    }
}
