using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Tests;

public sealed class ExternalMappingTests
{
    private const string Library = """
        using MapperForge;
        namespace Library;
        public class Source { public int Id { get; set; } }
        [MapFrom(typeof(Source))] public partial class DtoA { public int Id { get; set; } }
        [MapFrom(typeof(Source))] internal partial class HiddenDto { public int Id { get; set; } }
        public class Root
        {
            public Source Child { get; set; } = new();
            public Source? Optional { get; set; }
            public System.Collections.Generic.List<Source?>? Items { get; set; }
            public System.Collections.Generic.List<Source> Known { get; set; } = new();
        }
        """;

    [Fact]
    public void Executes_library_and_local_maps_and_collections_without_extension_ambiguity()
    {
        var library = GeneratorTestHost.RunNamed("Library", Library);
        ContractTestAssertions.Compiles(library);
        var consumer = GeneratorTestHost.RunNamed("Consumer", """
            using MapperForge;
            using Library;
            using System.Collections.Generic;
            [MapFrom(typeof(Source))] public partial class DtoB { public int Id { get; set; } }
            [MapFrom(typeof(Root))] public partial class LocalRoot
            {
                public DtoA Child { get; set; } = new();
                public DtoA? Optional { get; set; }
                public List<DtoA?>? Items { get; set; }
                public IReadOnlyList<DtoA> Known { get; set; } = new List<DtoA>();
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var source = new Source { Id = 42 };
                    IEnumerable<Source> items = new[] { source };
                    var full = LocalRoot.From(new Root { Child = source, Optional = source,
                        Items = new() { source, null }, Known = new() { source } });
                    var empty = LocalRoot.From(new Root());
                    return DtoA.From(source).Id == 42 && DtoB.From(source).Id == 42 &&
                        source.MapTo<DtoA>().Id == 42 && source.MapTo<DtoB>().Id == 42 &&
                        items.MapTo<DtoA>()[0].Id == 42 && items.MapTo<DtoB>()[0].Id == 42 &&
                        items.MapToList<DtoA>()[0].Id == 42 && items.MapToList<DtoB>()[0].Id == 42 &&
                        full.Child.Id == 42 && full.Optional?.Id == 42 && full.Items?[0]?.Id == 42 &&
                        full.Items[1] is null && full.Known[0].Id == 42 &&
                        empty.Optional is null && empty.Items is null;
                }
            }
            """, GeneratorTestHost.CreateReference(library));
        ContractTestAssertions.Compiles(consumer);
        GeneratorTestHost.Execute<bool>(consumer, library).Should().BeTrue();
        string.Join("\n", consumer.GeneratedSources).Should().Contain("global::Library.DtoA.From(")
            .And.NotContain("partial class DtoA").And.NotContain("HiddenDto");
        var libraryHelper = library.Compilation.GetTypeByMetadataName(HelperName(library))!;
        libraryHelper.DeclaredAccessibility.Should().Be(Accessibility.Internal);
        HelperName(library).Should().NotBe(HelperName(consumer));
    }

    [Fact]
    public void Generates_conveniences_for_external_maps_without_local_requests()
    {
        var library = GeneratorTestHost.RunNamed("Library", Library);
        var consumer = GeneratorTestHost.Run("""
            using MapperForge;
            public static class Probe
            {
                public static int Run() => new Library.Source { Id = 42 }.MapTo<Library.DtoA>().Id;
            }
            """, GeneratorTestHost.CreateReference(library));
        ContractTestAssertions.Compiles(consumer);
        consumer.GeneratedSources.Should().HaveCount(1);
        GeneratorTestHost.Execute<int>(consumer, library).Should().Be(42);
        consumer.Compilation.Assembly.GetAttributes().Where(IsMapAttribute).Should().BeEmpty();
    }

    [Fact]
    public void Emits_only_valid_public_pairs_with_versioned_unique_ordered_direct_dependencies()
    {
        var result = GeneratorTestHost.Run(Library + """

            [MapFrom(typeof(Root))] public partial class RootDto
            {
                public DtoA? Optional { get; set; }
                public System.Collections.Generic.List<DtoA?>? Items { get; set; }
                public System.Collections.Generic.IReadOnlyList<DtoA> Known { get; set; } = new System.Collections.Generic.List<DtoA>();
                internal HiddenDto Child { get; set; } = new();
            }
            [MapFrom(typeof(Source))] public partial class InvalidDto { public string Id { get; set; } = ""; }
            """);
        ContractTestAssertions.Rejects(result, "MFG003");
        var attributes = result.Compilation.Assembly.GetAttributes().Where(IsMapAttribute).ToArray();
        attributes.Should().HaveCount(2);
        attributes.Select(attribute => ((ITypeSymbol)attribute.ConstructorArguments[1].Value!).Name)
            .Should().BeEquivalentTo("DtoA", "RootDto");
        attributes.Should().OnlyContain(attribute => (int)attribute.ConstructorArguments[2].Value! == 1);
        attributes.Single(attribute => ((ITypeSymbol)attribute.ConstructorArguments[1].Value!).Name == "DtoA")
            .ConstructorArguments[3].Values.Should().BeEmpty();
        var root = attributes.Single(attribute => ((ITypeSymbol)attribute.ConstructorArguments[1].Value!).Name == "RootDto");
        root.ConstructorArguments[3].Values.Select(value => ((ITypeSymbol)value.Value!).Name)
            .Should().Equal("Source", "DtoA", "Source", "HiddenDto");
        var consumer = GeneratorTestHost.Run("", GeneratorTestHost.CreateReference(result));
        ContractTestAssertions.Compiles(consumer);
        string.Join("\n", consumer.GeneratedSources).Should().Contain("global::Library.RootDto.From(source)")
            .And.NotContain("HiddenDto").And.NotContain("InvalidDto");
    }

    [Theory]
    [InlineData("2", "", "public static Dto From(Source value) => new();")]
    [InlineData("1", ", typeof(Source)", "public static Dto From(Source value) => new();")]
    [InlineData("1", ", null!", "public static Dto From(Source value) => new();")]
    [InlineData("1", ", typeof(Source), typeof(Dto), typeof(Source), typeof(Dto)", "public static Dto From(Source value) => new();")]
    [InlineData("1", ", typeof(Source<>), typeof(Dto)", "public static Dto From(Source value) => new();")]
    [InlineData("1", ", typeof(Source), typeof(Source), typeof(Source), typeof(Dto)", "public static Dto From(Source value) => new();")]
    [InlineData("1", "", "public Dto From(Source value) => new();")]
    [InlineData("1", "", "internal static Dto From(Source value) => new();")]
    [InlineData("1", "", "public static string From(Source value) => \"\";")]
    [InlineData("1", "", "public static Dto? From(Source value) => null;")]
    [InlineData("1", "", "public static Dto From(ref Source value) => new();")]
    [InlineData("1", "", "public static Dto From<T>(Source value) => new();")]
    [InlineData("1", "", "public static Dto From(object value) => new();")]
    [InlineData("1", "", "public static Dto From(Source value, int extra) => new();")]
    [InlineData("1", "", "public static Dto From(Source value, __arglist) => new();")]
    [InlineData("1", "", "public static ref Dto From(Source value) => ref holder; private static Dto holder = new();")]
    public void Rejects_required_incompatible_metadata_but_preserves_independent_maps(
        string version, string dependencies, string method)
    {
        var library = GeneratorTestHost.RunNamed("BadLibrary", $$"""
            using MapperForge;
            [assembly: MapperForgeMapping(typeof(Source), typeof(Dto), {{version}}{{dependencies}})]
            public class Source;
            public class Source<T>;
            public class Dto { {{method}} }
            """);
        ContractTestAssertions.Compiles(library);
        var reference = GeneratorTestHost.CreateReference(library);
        var used = GeneratorTestHost.Run("""
            using MapperForge;
            public class Root { public Source Child { get; set; } = new(); }
            [MapFrom(typeof(Root))] public partial class RootDto { public Dto Child { get; set; } = new(); }
            public class GoodSource { public int Id => 42; }
            [MapFrom(typeof(GoodSource))] public partial class GoodDto { public int Id { get; set; } }
            public static class Probe { public static int Run() => new GoodSource().MapTo<GoodDto>().Id; }
            """, reference);
        ContractTestAssertions.Rejects(used, "MFG011");
        used.Diagnostics.Where(diagnostic => diagnostic.Id == "MFG011").Should().ContainSingle()
            .Which.Location.IsInSource.Should().BeTrue();
        used.GeneratedSources.Should().HaveCount(2);
        GeneratorTestHost.Execute<int>(used, library).Should().Be(42);

        var unused = GeneratorTestHost.Run("""
            using MapperForge;
            public class GoodSource;
            [MapFrom(typeof(GoodSource))] public partial class GoodDto;
            """, reference);
        ContractTestAssertions.Compiles(unused);
        string.Join("\n", unused.GeneratedSources).Should().NotContain("global::Dto.From");
    }

    [Fact]
    public void Reports_incompatible_element_map_at_the_collection_property()
    {
        var reference = GeneratorTestHost.CreateReference("""
            using MapperForge;
            [assembly: MapperForgeMapping(typeof(Source), typeof(Dto), 42)]
            public class Source;
            public class Dto { public static Dto From(Source source) => new(); }
            """);
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Root { public System.Collections.Generic.List<Source> Items { get; set; } = new(); }
            [MapFrom(typeof(Root))] public partial class RootDto
            { public System.Collections.Generic.List<Dto> Items { get; set; } = new(); }
            """, reference);
        ContractTestAssertions.Rejects(result, "MFG011");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_references_and_identical_attributes_do_not_duplicate_dispatch()
    {
        var library = GeneratorTestHost.RunNamed("Library", """
            using MapperForge;
            [assembly: MapperForgeMapping(typeof(Source), typeof(Dto), 1)]
            [assembly: MapperForgeMapping(typeof(Source), typeof(Dto), 1)]
            public class Source;
            public class Dto { public static Dto From(Source source) => new(); }
            """);
        var reference = GeneratorTestHost.CreateReference(library);
        var result = GeneratorTestHost.Run("", reference, reference);
        ContractTestAssertions.Compiles(result);
        result.GeneratedSources.Should().HaveCount(1);
        result.GeneratedSources[0].Split("return (TDestination)(object)(global::Dto)global::Dto.From(source);").Should().HaveCount(2);
    }

    [Fact]
    public void Conflicting_versions_of_the_same_pair_are_unavailable()
    {
        var reference = GeneratorTestHost.CreateReference("""
            using MapperForge;
            [assembly: MapperForgeMapping(typeof(Source), typeof(Dto), 1)]
            [assembly: MapperForgeMapping(typeof(Source), typeof(Dto), 2)]
            public class Source;
            public class Dto { public static Dto From(Source source) => new(); }
            """);
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Root { public Source Child { get; set; } = new(); }
            [MapFrom(typeof(Root))] public partial class RootDto { public Dto Child { get; set; } = new(); }
            """, reference);
        ContractTestAssertions.Rejects(result, "MFG011");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Preserves_nullable_to_nonnullable_errors_for_external_maps()
    {
        var reference = GeneratorTestHost.CreateReference(Library);
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            [MapFrom(typeof(Library.Root))] public partial class LocalRoot
            {
                public Library.DtoA Optional { get; set; } = new();
                public System.Collections.Generic.List<Library.DtoA>? Items { get; set; }
            }
            """, reference);
        ContractTestAssertions.Rejects(result, "MFG008");
        result.Diagnostics.Count(diagnostic => diagnostic.Id == "MFG008").Should().Be(2);
    }

    [Fact]
    public void Does_not_export_or_discover_effectively_internal_types_even_with_friend_access()
    {
        var library = GeneratorTestHost.RunNamed("Library", """
            using MapperForge;
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]
            [assembly: MapperForgeMapping(typeof(Source), typeof(Dto), 1)]
            internal class Source;
            public class Dto { internal static Dto From(Source source) => new(); }
            """);
        var consumer = GeneratorTestHost.RunNamed("Consumer", """
            using MapperForge;
            internal class Root { public Source Child { get; set; } = new(); }
            [MapFrom(typeof(Root))] internal partial class RootDto { public Dto Child { get; set; } = new(); }
            """, GeneratorTestHost.CreateReference(library));
        ContractTestAssertions.Rejects(consumer, "MFG011");
        consumer.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Accepts_assignable_return_and_preserves_static_dispatch_for_closed_generic_sources()
    {
        var library = GeneratorTestHost.RunNamed("Library", """
            using MapperForge;
            [assembly: MapperForgeMapping(typeof(Source<int>), typeof(Dto), 1)]
            public class Source<T> { public T Value { get; set; } = default!; }
            public class Dto
            {
                public int Value { get; set; }
                public static DerivedDto From(Source<int> source) => new() { Value = source.Value };
            }
            public class DerivedDto : Dto;
            """);
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public static class Probe
            {
                public static bool Run()
                {
                    if (new Source<int> { Value = 42 }.MapTo<Dto>().Value != 42) return false;
                    try { new Source<string> { Value = "42" }.MapTo<Dto>(); return false; }
                    catch (MapperForgeMappingException) { return true; }
                }
            }
            """, GeneratorTestHost.CreateReference(library));
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result, library).Should().BeTrue();
    }

    [Fact]
    public void Converts_assignable_external_returns_before_generic_dispatch_boxes_the_value()
    {
        var library = GeneratorTestHost.RunNamed("Library", """
            using MapperForge;
            [assembly: MapperForgeMapping(typeof(Source), typeof(Dto), 1)]
            public class Source { public int Id => 42; }
            public class Dto
            {
                public int Id { get; set; }
                public static Result From(Source source) => new() { Id = source.Id };
            }
            public class Result
            {
                public int Id { get; set; }
                public static implicit operator Dto(Result value) => new() { Id = value.Id };
            }
            """);
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Root { public Source Child { get; set; } = new(); }
            [MapFrom(typeof(Root))] public partial class RootDto { public Dto Child { get; set; } = new(); }
            public static class Probe
            {
                public static bool Run() => new Source().MapTo<Dto>().Id == 42 &&
                    new System.Collections.Generic.List<Source> { new() }.MapToList<Dto>()[0].Id == 42 &&
                    RootDto.From(new Root()).Child.Id == 42;
            }
            """, GeneratorTestHost.CreateReference(library));
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result, library).Should().BeTrue();
    }

    [Fact]
    public void Does_not_walk_transitive_references()
    {
        var leaf = GeneratorTestHost.RunNamed("Leaf", Library);
        var bridge = GeneratorTestHost.RunNamed("Bridge", "public class Bridge { public Library.Source Source => new(); }",
            GeneratorTestHost.CreateReference(leaf));
        var result = GeneratorTestHost.Run("", GeneratorTestHost.CreateReference(bridge));
        ContractTestAssertions.Compiles(result);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_external_scalar_enumerable_dispatch_that_conflicts_with_local_collection_helpers()
    {
        var library = GeneratorTestHost.RunNamed("Library", """
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(System.Collections.Generic.IEnumerable<Source>))] public partial class CollectionDto;
            """);
        ContractTestAssertions.Compiles(library);
        var consumer = GeneratorTestHost.Run("""
            using MapperForge;
            [MapFrom(typeof(Source))] public partial class ElementDto { public int Id { get; set; } }
            public class Root { public System.Collections.Generic.IEnumerable<Source> Items { get; set; } = new Source[0]; }
            [MapFrom(typeof(Root))] public partial class RootDto { public CollectionDto Items { get; set; } = new(); }
            public static class Probe
            {
                public static int Run() => new System.Collections.Generic.List<Source> { new() }.MapTo<ElementDto>()[0].Id;
            }
            """, GeneratorTestHost.CreateReference(library));
        ContractTestAssertions.Rejects(consumer, "MFG011");
        consumer.GeneratedSources.Should().HaveCount(2);
        GeneratorTestHost.Execute<int>(consumer, library).Should().Be(42);
    }

    [Fact]
    public void Calls_in_generated_helpers_do_not_consult_other_extension_candidates()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(Source))] public partial class Dto { public int Id { get; set; } }
            public static class OtherExtensions
            {
                public static T MapTo<T>(this Source source) => throw new System.Exception();
                public static System.Collections.Generic.List<T> MapToList<T>(this System.Collections.Generic.IEnumerable<Source> source)
                    => throw new System.Exception();
            }
            public class Root { public System.Collections.Generic.List<Source> Items { get; set; } = new() { new() }; }
            [MapFrom(typeof(Root))] public partial class RootDto { public System.Collections.Generic.List<Dto> Items { get; set; } = new(); }
            public static class Probe { public static int Run() => RootDto.From(new Root()).Items[0].Id; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<int>(result).Should().Be(42);
        string.Join("\n", result.GeneratedSources).Should().NotContain("item.MapTo<").And.NotContain("source.MapToList<");
    }

    private static bool IsMapAttribute(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() == "MapperForge.MapperForgeMappingAttribute";

    private static string HelperName(GeneratorRunResult result) => result.Compilation.Assembly.GlobalNamespace
        .GetNamespaceMembers().Single(ns => ns.Name == "MapperForge").GetTypeMembers()
        .Single(type => type.Name.StartsWith("MapperForgeGeneratedExtensions_")).ToDisplayString();
}
