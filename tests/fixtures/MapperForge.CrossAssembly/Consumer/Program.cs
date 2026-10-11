using Fixture;
using MapperForge;

var source = new Source { Id = 42 };
IEnumerable<Source> items = new[] { source };
var mapped = LocalRoot.From(new Root
{
    Child = source, Optional = source, Items = new() { source, null }, Known = new() { source }
});
var empty = LocalRoot.From(new Root());
if (DtoA.From(source).Id != 42 || DtoB.From(source).Id != 42 ||
    source.MapTo<DtoA>().Id != 42 || source.MapTo<DtoB>().Id != 42 ||
    items.MapTo<DtoA>()[0].Id != 42 || items.MapTo<DtoB>()[0].Id != 42 ||
    items.MapToList<DtoA>()[0].Id != 42 || items.MapToList<DtoB>()[0].Id != 42 ||
    mapped.Child.Id != 42 || mapped.Optional?.Id != 42 || mapped.Items?[0]?.Id != 42 ||
    mapped.Items[1] is not null || mapped.Known[0].Id != 42 || empty.Optional is not null || empty.Items is not null ||
    new Root { Child = source }.MapTo<LibraryRootDto>().Child.Id != 42)
    throw new InvalidOperationException("Cross-assembly mapping failed.");

try { DtoA.From(null!); throw new InvalidOperationException("Expected null rejection."); }
catch (ArgumentNullException) { }
try { source.MapTo<SignatureDto>(); throw new InvalidOperationException("Expected unavailable map."); }
catch (MapperForgeMappingException) { }

var immutable = source.MapTo<ImmutableDto>();
var constructed = ConstructedRoot.From(new Root
{
    Child = source, Items = new() { source, null }, Known = new() { source }
});
var libraryImmutable = new Root { Child = source }.MapTo<ImmutableRootDto>();
if (immutable.Id != 42 || items.MapToList<ImmutableDto>()[0].Id != 42 ||
    constructed.Child.Id != 42 || constructed.Optional is not null || constructed.Items?[0]?.Id != 42 ||
    constructed.Items[1] is not null || constructed.Known[0].Id != 42 ||
    libraryImmutable.Child.Id != 42 || libraryImmutable.Optional is not null)
    throw new InvalidOperationException("Immutable cross-assembly mapping failed.");

Console.WriteLine("Cross-assembly consumer passed: From, MapTo, collections, nested maps, immutable construction and null policy.");

[MapFrom(typeof(Source))]
public partial class DtoB { public int Id { get; init; } }

[MapFrom(typeof(Root))]
public partial class LocalRoot
{
    public DtoA Child { get; init; } = new();
    public DtoA? Optional { get; init; }
    public List<DtoA?>? Items { get; init; }
    public IReadOnlyList<DtoA> Known { get; init; } = new List<DtoA>();
}

[MapFrom(typeof(Root))]
public partial record ConstructedRoot(
    ImmutableDto Child, ImmutableDto? Optional, List<ImmutableDto?>? Items, IReadOnlyList<ImmutableDto> Known);
