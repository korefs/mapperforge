namespace MapperForge;

/// <summary>
/// Marks a partial concrete destination for generation of a static From(TSource) mapping.
/// May be combined with MapFromAttribute; identical mapping pairs are generated once.
/// </summary>
public interface IMapFrom<TSource>
{
}
