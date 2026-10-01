using MapperForge;

var source = new Source { Id = 42 };
Assert(Destination.From(source).Id == 42, "From");
Assert(source.MapTo<Destination>().Id == 42, "MapTo");
Assert(new[] { source }.MapToList<Destination>().Single().Id == 42, "MapToList");
Assert(MarkerDestination.From(source).Id == 42, "marker From");
Assert(source.MapTo<MarkerDestination>().Id == 42, "marker MapTo");
Assert(new[] { source }.MapToList<MarkerDestination>().Single().Id == 42, "marker MapToList");
Console.WriteLine("Package consumer passed: From, MapTo, MapToList, marker and attribute deduplication.");

static void Assert(bool condition, string api)
{
    if (!condition) throw new InvalidOperationException($"Package consumer failed: {api}");
}

public class SourceBase { public int Id { get; set; } }
public sealed class Source : SourceBase;
public class DestinationBase { public int Id { get; set; } }
[MapFrom(typeof(Source))]
public partial class Destination : DestinationBase, IMapFrom<Source>;
public partial class MarkerDestination : IMapFrom<Source> { public int Id { get; init; } }
