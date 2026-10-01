using MapperForge;

var source = new Source { Id = 42 };
Assert(Destination.From(source).Id == 42, "From");
Assert(source.MapTo<Destination>().Id == 42, "MapTo");
Assert(new[] { source }.MapToList<Destination>().Single().Id == 42, "MapToList");
Console.WriteLine("Package consumer passed: From, MapTo, MapToList.");

static void Assert(bool condition, string api)
{
    if (!condition) throw new InvalidOperationException($"Package consumer failed: {api}");
}

public class SourceBase { public int Id { get; set; } }
public sealed class Source : SourceBase;
public class DestinationBase { public int Id { get; set; } }
[MapFrom(typeof(Source))]
public partial class Destination : DestinationBase;
