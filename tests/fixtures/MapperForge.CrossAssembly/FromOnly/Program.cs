using Fixture;
using MapperForge;

if (DtoA.From(new Source { Id = 42 }).Id != 42 ||
    typeof(UnmappedDto).GetMethod("From") is not null ||
    typeof(UnmappedDto).Assembly.GetTypes().Any(type => type.Name.StartsWith("MapperForgeGeneratedExtensions")))
    throw new InvalidOperationException("From-only consumer or analyzer exclusion failed.");
Console.WriteLine("From-only consumer passed without running the analyzer.");

[MapFrom(typeof(Source))]
public partial class UnmappedDto { public int Id { get; init; } }
