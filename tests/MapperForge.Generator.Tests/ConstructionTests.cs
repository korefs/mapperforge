using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Tests;

public sealed class ConstructionTests
{
    [Theory]
    [InlineData("class", "public Destination(int id, string name) { Id = id; Name = name; } public int Id { get; } public string Name { get; }")]
    [InlineData("record", "public Destination(int id, string name) { Id = id; Name = name; } public int Id { get; } public string Name { get; }")]
    [InlineData("struct", "public Destination(int id, string name) { Id = id; Name = name; } public int Id { get; } public string Name { get; }")]
    public void Executes_getter_only_models_with_private_or_public_construction(string kind, string members)
    {
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            public class Source { public int Id => 42; public string Name => "Ada"; }
            [MapFrom(typeof(Source))] public partial {{kind}} Destination { {{members}} }
            public static class Probe { public static bool Run() =>
                Destination.From(new Source()).Id == 42 && new Source().MapTo<Destination>().Name == "Ada"; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("record")]
    [InlineData("record struct")]
    [InlineData("readonly record struct")]
    public void Executes_positional_records_with_property_attributes_and_init(string kind)
    {
        var declaration = kind.StartsWith("readonly", StringComparison.Ordinal) ? "readonly partial record struct" : "partial " + kind;
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            public class Source { public int Id => 42; public string DisplayName => " ada "; public int Age => 7; }
            [MapFrom(typeof(Source))] public {{declaration}} Destination(
                int Id, [property: MapProperty("DisplayName"), MapTransform("Normalize")] string Name)
            {
                public int Age { get; init; }
                private static string Normalize(string value) => value.Trim().ToUpperInvariant();
            }
            public static class Probe { public static bool Run() =>
                new Source().MapTo<Destination>() is { Id: 42, Name: "ADA", Age: 7 }; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Does_not_invoke_a_setter_twice_and_combines_constructor_with_initializer()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public int Id => 42; public string Name => "Ada"; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                private Destination(int id) { Id = id; }
                private int id;
                public int Id { get => id; set { id = value; Calls++; } }
                [MapIgnore] public int Calls { get; private set; }
                public string Name { get; init; } = "";
            }
            public static class Probe { public static bool Run() =>
                Destination.From(new Source()) is { Id: 42, Name: "Ada", Calls: 1 }; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_ranking_is_independent_of_declaration_order(bool reverse)
    {
        var first = "private Destination(int id) { Id = id; Mode = 1; }";
        var second = "private Destination(int id, string name) { Id = id; Mode = name == \"Ada\" ? 2 : -1; }";
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            public class Source { public int Id => 42; public string Name => "Ada"; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                {{(reverse ? second : first)}} {{(reverse ? first : second)}}
                public int Id { get; } [MapIgnore] public int Mode { get; }
            }
            public static class Probe { public static bool Run() => Destination.From(new Source()) is { Id: 42, Mode: 2 }; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("int id, string name = \"fallback\"", "string name, int id", 2)]
    [InlineData("int id", "int id, string name = \"fallback\"", 2)]
    [InlineData("int id, string name", "int id, string name, int missing = 9", 0)]
    public void Ranks_resolved_required_then_total_parameters_and_rejects_final_ties(
        string first, string second, int expected)
    {
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            public class Source { public int Id => 42; public string Name => "Ada"; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                private Destination({{first}}) { Id = id; Mode = 1; }
                private Destination({{second}}) { Id = id; Mode = 2; }
                public int Id { get; } [MapIgnore] public int Mode { get; }
            }
            {{(expected == 0 ? "" : "public static class Probe { public static int Run() => Destination.From(new Source()).Mode; }")}}
            """);
        if (expected == 0)
        {
            ContractTestAssertions.Rejects(result, "MFG007");
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.GetMessage(null).Contains("ambiguous constructors"));
            result.GeneratedSources.Should().BeEmpty();
        }
        else
        {
            ContractTestAssertions.Compiles(result);
            // With equal required counts the candidate resolving more optional parameters wins.
            GeneratorTestHost.Execute<int>(result).Should().Be(expected);
        }
    }

    [Fact]
    public void Preserves_a_valid_empty_constructor_but_uses_arguments_for_getter_only_members()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(Source))] public partial class Mutable
            {
                public Mutable() { Mode = 1; } public Mutable(int id) { Id = id; Mode = 2; }
                public int Id { get; init; } [MapIgnore] public int Mode { get; }
            }
            [MapFrom(typeof(Source))] public partial class Immutable
            {
                public Immutable() { Id = -1; } public Immutable(int id) { Id = id; }
                public int Id { get; }
            }
            public static class Probe { public static bool Run() =>
                Mutable.From(new Source()) is { Id: 42, Mode: 1 } && Immutable.From(new Source()).Id == 42; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Omits_optional_arguments_and_ignored_properties_preserving_defaults()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public int Id => 42; public int Version => 100; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                private Destination(int id, int missing = 7, int version = 9, params int[] extra)
                { Id = id; Missing = missing; Version = version; Extra = extra.Length; }
                public int Id { get; } public int Missing { get; }
                [MapIgnore] public int Version { get; } [MapIgnore] public int Extra { get; }
            }
            public static class Probe { public static bool Run() =>
                Destination.From(new Source()) is { Id: 42, Missing: 7, Version: 9, Extra: 0 }; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("public int ID => 42; public int Id => 7;", "int Id", 7)]
    [InlineData("public int ID => 42;", "int id", 42)]
    public void Resolves_unassociated_source_names_exactly_then_by_unique_case_insensitive_match(
        string source, string parameter, int expected)
    {
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            public class Source { {{source}} }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                private Destination({{parameter}}) { Value = {{parameter.Split(' ')[1]}}; }
                [MapIgnore] public int Value { get; }
            }
            public static class Probe { public static int Run() => Destination.From(new Source()).Value; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<int>(result).Should().Be(expected);
    }

    [Theory]
    [InlineData("public int Id => 1; public int ID => 2;", "public Destination(int id) {}", "source property association")]
    [InlineData("public int Id => 1;", "public Destination(int id) {} public int Id { get; init; } public int ID { get; init; }", "destination property association")]
    [InlineData("public int Id => 1;", "public Destination(int id) {} [MapIgnore] public int Id { get; }", "associated property is ignored")]
    [InlineData("public int Id => 1;", "public Destination(int missing) {}", "source property 'missing' is missing")]
    [InlineData("public int Id => 1;", "public Destination(ref int id) {}", "ref and pointer parameters")]
    public void Rejects_invalid_candidates_contextually(string source, string destination, string reason)
    {
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            public class Source { {{source}} }
            [MapFrom(typeof(Source))] public partial class Destination { {{destination}} }
            """);
        ContractTestAssertions.Rejects(result, "MFG007");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.GetMessage(null).Contains(reason));
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Exact_destination_property_association_wins_over_other_casings()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                private Destination(int Id) { this.Id = Id; }
                public int Id { get; } [MapIgnore] public int ID { get; init; }
            }
            public static class Probe { public static int Run() => Destination.From(new Source()).Id; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<int>(result).Should().Be(42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Required_constructor_values_follow_the_compiler_contract(bool setsRequired)
    {
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            using System.Diagnostics.CodeAnalysis;
            public class Source { public int Id => 42; }
            public class Base { public required int Id { get; init; } }
            [MapFrom(typeof(Source))] public partial class Destination : Base
            {
                {{(setsRequired ? "[SetsRequiredMembers]" : "")}}
                private Destination(int id) { Id = id; }
            }
            public static class Probe { public static int Run() => Destination.From(new Source()).Id; }
            """);
        ContractTestAssertions.Compiles(result);
        var mapping = result.GeneratedSources.Single(source => source.Contains("partial class Destination"));
        mapping.Should().Contain("(int)(source.Id)");
        if (setsRequired) mapping.Should().NotContain("Id = source.Id");
        else mapping.Should().Contain("Id = source.Id");
        GeneratorTestHost.Execute<int>(result).Should().Be(42);
    }

    [Theory]
    [InlineData("[MapIgnore] public required int Id { get; init; }", "int id")]
    [InlineData("public required int Missing { get; init; }", "int id")]
    [InlineData("public required int Id;", "int id")]
    public void Rejects_missing_or_ignored_required_members_with_parameterized_construction(string member, string parameter)
    {
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(Source))] public partial class Destination
            { public Destination({{parameter}}) {} {{member}} }
            """);
        ContractTestAssertions.Rejects(result, "MFG007");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void SetsRequiredMembers_covers_ignored_inherited_members_and_fields()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            using System.Diagnostics.CodeAnalysis;
            public class Source { public int Version => 42; }
            public class Base { [MapIgnore] public required int Id { get; init; } }
            [MapFrom(typeof(Source))] public partial class Destination : Base
            {
                public required int Code;
                [SetsRequiredMembers] private Destination(int version) { Id = version; Code = version; }
            }
            public static class Probe { public static bool Run() =>
                Destination.From(new Source()) is { Id: 42, Code: 42 }; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("public string? Name => null;", "string Name", "MFG008")]
    [InlineData("public string Name => \"Ada\";", "int Name", "MFG003")]
    [InlineData("public string Name => \"Ada\";", "[property: MapTransform(\"Missing\")] string Name", "MFG004")]
    public void Constructor_arguments_reuse_conversion_nullability_and_transform_diagnostics(
        string source, string parameter, string diagnostic)
    {
        var result = GeneratorTestHost.Run($$"""
            using MapperForge;
            public class Source { {{source}} }
            [MapFrom(typeof(Source))] public partial record Destination({{parameter}});
            """);
        ContractTestAssertions.Rejects(result, diagnostic);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Nullable_constructor_transforms_support_fallback_and_lifting()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public string? Name => null; public string? Alias => null; }
            [MapFrom(typeof(Source))] public partial record Destination(
                [property: MapTransform("Fallback")] string Name,
                [property: MapTransform("Upper")] string? Alias)
            {
                private static string Fallback(string? value) => value ?? "default";
                private static string Upper(string value) => value.ToUpperInvariant();
            }
            public static class Probe { public static bool Run() =>
                Destination.From(new Source()) is { Name: "default", Alias: null }; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Omits_an_optional_gap_before_a_mapped_argument()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                private Destination(string missing = "default", int id = 7) { Id = id; Label = missing; }
                public int Id { get; } [MapIgnore] public string Label { get; }
            }
            public static class Probe { public static bool Run() => Destination.From(new Source()) is { Id: 42, Label: "default" }; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_call_ambiguous_after_omitting_optional_arguments()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public string Id => "incompatible"; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                private Destination(int id = 7) {} private Destination(string missing = "default") {}
            }
            """);
        ContractTestAssertions.Rejects(result, "MFG007");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.GetMessage(null).Contains("cannot be invoked unambiguously"));
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Resolves_constructor_arguments_with_conversion_and_escaped_identifiers()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public interface IBase { int @int { get; } }
            public interface Source : IBase;
            public class Concrete : Source { public int @int => 42; }
            [MapFrom(typeof(Source))] public partial record Destination(long @int);
            public static class Probe { public static long Run() => Destination.From(new Concrete()).@int; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<long>(result).Should().Be(42);
    }

    [Fact]
    public void Private_constructor_in_another_partial_maps_renamed_transformed_getter()
    {
        var result = GeneratorTestHost.Run("""
            using MapperForge;
            public class Source { public string DisplayName => "Ada"; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                [MapProperty("DisplayName"), MapTransform(nameof(Upper))] public string Name { get; }
                private static string Upper(string value) => value.ToUpperInvariant();
            }
            public partial class Destination { private Destination(string name) { Name = name; } }
            public static class Probe { public static string Run() => Destination.From(new Source()).Name; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<string>(result).Should().Be("ADA");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_dependencies_work_locally_and_across_assemblies(bool external)
    {
        const string librarySource = """
            using MapperForge;
            public class Child { public int Id => 42; }
            [MapFrom(typeof(Child))] public partial record ChildDto(int Id);
            """;
        var library = GeneratorTestHost.RunNamed("Construction.Library", librarySource);
        ContractTestAssertions.Compiles(library);
        var consumer = GeneratorTestHost.RunNamed("Construction.Consumer", (external ? "" : librarySource) + """
            public class Source
            {
                public Child Child => new(); public Child? Optional => null;
                public System.Collections.Generic.List<Child?>? Items => new() { new(), null };
                public System.Collections.Generic.List<Child> Known => new() { new() };
            }
            [MapperForge.MapFrom(typeof(Source))] public partial record Destination(
                ChildDto Child, ChildDto? Optional, System.Collections.Generic.List<ChildDto?>? Items,
                System.Collections.Generic.IReadOnlyList<ChildDto> Known);
            public static class Probe { public static bool Run() =>
                Destination.From(new Source()) is { Child.Id: 42, Optional: null } dto &&
                dto.Items![0]!.Id == 42 && dto.Items[1] is null && dto.Known[0].Id == 42; }
            """, external ? new[] { GeneratorTestHost.CreateReference(library) } : Array.Empty<MetadataReference>());
        ContractTestAssertions.Compiles(consumer);
        GeneratorTestHost.Execute<bool>(consumer, library).Should().BeTrue();
        var metadata = consumer.Compilation.Assembly.GetAttributes().Single(attribute =>
            attribute.AttributeClass?.Name == "MapperForgeMappingAttribute" &&
            ((ITypeSymbol)attribute.ConstructorArguments[1].Value!).Name == "Destination");
        metadata.ConstructorArguments[3].Values.Should().HaveCount(2);
    }
}
