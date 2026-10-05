using MapperForge;
#if INVALID_VERSION
using ChildSource = Fixture.VersionSource;
using ChildDto = Fixture.VersionDto;
#elif INVALID_DEPENDENCIES
using ChildSource = Fixture.DependencySource;
using ChildDto = Fixture.DependencyDto;
#else
using ChildSource = Fixture.SignatureSource;
using ChildDto = Fixture.SignatureDto;
#endif

public class LocalSource { public ChildSource Child { get; set; } = new(); }
[MapFrom(typeof(LocalSource))]
public partial class LocalDto { public ChildDto Child { get; set; } = new(); }
