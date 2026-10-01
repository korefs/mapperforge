using FluentAssertions;

namespace MapperForge.Generator.Tests;

public sealed class MarkerInterfaceTests
{
    [Fact]
    public void Combines_marker_interfaces_and_attributes_without_duplicate_maps()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source<T> { public int Id => 42; }
            public class Other { public int Id => 7; }
            public interface IMarker : IMapFrom<Source<int>>;
            public partial class First : IMapFrom<Source<int>> { public int Id { get; set; } }
            [MapFrom(typeof(Source<int>)), MapFrom(typeof(Other))]
            public partial class Second : IMarker, IMapFrom<Other> { public int Id { get; set; } }
            [MapFrom(typeof(Other))] public partial class Second;
            public static class Probe
            {
                public static bool Run() => First.From(new Source<int>()).Id == 42 &&
                    Second.From(new Source<int>()).Id == 42 && Second.From(new Other()).Id == 7 &&
                    new Source<int>().MapTo<First>().Id == 42 && new Other().MapTo<Second>().Id == 7;
            }
            """);
        ContractTestAssertions.Compiles(result);
        result.GeneratedSources.Should().HaveCount(4);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Removes_placeholder_options_from_runtime_api()
    {
        typeof(MapFromAttribute).Assembly.GetType("MapperForge.MapperForgeOptions").Should().BeNull();
    }
}
