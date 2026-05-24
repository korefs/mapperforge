namespace MapperForge;

public sealed class MapperForgeMappingException : InvalidOperationException
{
    public MapperForgeMappingException(string message)
        : base(message)
    {
    }
}
