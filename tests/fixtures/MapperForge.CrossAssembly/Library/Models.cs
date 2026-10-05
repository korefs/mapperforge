using MapperForge;

[assembly: MapperForgeMapping(typeof(Fixture.VersionSource), typeof(Fixture.VersionDto), 99)]
[assembly: MapperForgeMapping(typeof(Fixture.DependencySource), typeof(Fixture.DependencyDto), 1, typeof(Fixture.Source))]
[assembly: MapperForgeMapping(typeof(Fixture.SignatureSource), typeof(Fixture.SignatureDto), 1)]

namespace Fixture;

public class Source { public int Id { get; set; } }

[MapFrom(typeof(Source))]
public partial class DtoA { public int Id { get; init; } }

[MapFrom(typeof(Source))]
internal partial class HiddenDto { public int Id { get; init; } }

public class Root
{
    public Source Child { get; set; } = new();
    public Source? Optional { get; set; }
    public List<Source?>? Items { get; set; }
    public List<Source> Known { get; set; } = new();
}

[MapFrom(typeof(Root))]
public partial class LibraryRootDto
{
    public DtoA Child { get; init; } = new();
    public DtoA? Optional { get; init; }
    public List<DtoA?>? Items { get; init; }
    public IReadOnlyList<DtoA> Known { get; init; } = new List<DtoA>();
}

public class VersionSource;
public class VersionDto { public static VersionDto From(VersionSource source) => new(); }
public class DependencySource;
public class DependencyDto { public static DependencyDto From(DependencySource source) => new(); }
public class SignatureSource;
public class SignatureDto { public static string From(SignatureSource source) => ""; }
