using FluentAssertions;

namespace MapperForge.Generator.Tests;

public sealed class ContractValidationTests
{
    [Theory]
    [InlineData("public abstract partial class Destination", "MFG006")]
    [InlineData("public static partial class Destination", "MFG006")]
    [InlineData("public ref partial struct Destination", "MFG006")]
    [InlineData("public partial class Destination<T>", "MFG006")]
    [InlineData("file partial class Destination", "MFG006")]
    [InlineData("public partial class Destination { public Destination(int id) {} }", "MFG007")]
    [InlineData("public partial record Destination(int Id)", "MFG007")]
    [InlineData("public partial class Destination { public Destination(int id = 0) {} public Destination(string name = \"\") {} }", "MFG007")]
    [InlineData("public partial class Destination { public static Destination From(Source source) => new(); }", "MFG006")]
    [InlineData("public partial class Destination { public Destination From(Source source) => new(); }", "MFG006")]
    [InlineData("public partial class Destination { public int From { get; set; } }", "MFG006")]
    [InlineData("public partial class Container<T> { [MapFrom(typeof(Source))] public partial class Destination; }", "MFG006")]
    [InlineData("public partial class Container { [MapFrom(typeof(Source))] private partial class Destination; }", "MFG006")]
    [InlineData("public class Container { [MapFrom(typeof(Source))] public partial class Destination; }", "MFG001")]
    public void Rejects_unsupported_destinations_without_generated_compiler_errors(string destination, string id)
    {
        var attribute = destination.StartsWith("public partial class Container", StringComparison.Ordinal) ||
            destination.StartsWith("public class Container", StringComparison.Ordinal) ? "" : "[MapFrom(typeof(Source))]";
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Source;
            {{attribute}}
            {{destination}};
            """);
        ContractTestAssertions.Rejects(result, id);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("public class Source<T>", "Source<>")]
    [InlineData("public ref struct Source", "Source")]
    [InlineData("file class Source", "Source")]
    [InlineData("public class Source", "int[]")]
    [InlineData("public class Source", "object")]
    public void Rejects_unsupported_source_shapes(string source, string sourceType)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            {{source}};
            [MapFrom(typeof({{sourceType}}))] public partial class Destination;
            """);
        ContractTestAssertions.Rejects(result, "MFG006");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Marker_interface_alone_is_a_contract_without_generated_destination_members()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source;
            public partial interface Destination : IMapFrom<Source>;
            """);
        ContractTestAssertions.Compiles(result);
        result.GeneratedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("record")]
    [InlineData("record struct")]
    [InlineData("readonly record struct")]
    public void Executes_simple_mutable_and_init_models(string kind)
    {
        var declaration = kind.StartsWith("readonly", StringComparison.Ordinal) ? "public readonly partial " + kind[9..] : "public partial " + kind;
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public struct Source { public int Id { get; init; } }
            [MapFrom(typeof(Source))] {{declaration}} Destination { public int Id { get; init; } }
            public static class Probe
            {
                public static bool Run() => Destination.From(new Source { Id = 42 }).Id == 42 &&
                    new Source { Id = 7 }.MapTo<Destination>().Id == 7;
            }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("internal", "internal", "")]
    [InlineData("internal", "public", "")]
    [InlineData("public", "internal", "")]
    [InlineData("public", "public", "internal partial class Container {")]
    public void Adjusts_effective_accessibility_for_internal_sources_destinations_and_containers(
        string sourceAccess, string destinationAccess, string container)
    {
        var destinationName = container.Length == 0 ? "Destination" : "Container.Destination";
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            {{sourceAccess}} class Source { public int Id => 42; }
            {{container}}
            [MapFrom(typeof(Source))] {{destinationAccess}} partial class Destination { public int Id { get; set; } }
            {{(container.Length == 0 ? "" : "}")}}
            public static class Probe { public static int Run() => {{destinationName}}.From(new Source()).Id; }
            """);
        ContractTestAssertions.Compiles(result);
        string.Join("\n", result.GeneratedSources).Should().Contain("internal static");
        GeneratorTestHost.Execute<int>(result).Should().Be(42);
    }

    [Fact]
    public void Escapes_namespace_container_type_property_and_transform_identifiers()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            namespace @namespace.@event
            {
                public class @class { public int @int => 42; }
                public partial class @struct
                {
                    [MapFrom(typeof(@class))]
                    public partial class @record
                    {
                        [MapTransform(nameof(@return))] public int @int { get; private set; }
                        private static int @return(int value) => value;
                    }
                }
            }
            public static class Probe
            {
                public static int Run() => @namespace.@event.@struct.@record.From(new @namespace.@event.@class()).@int;
            }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<int>(result).Should().Be(42);
    }

    [Fact]
    public void Invokes_private_and_optional_constructors_from_the_partial_context()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(Source))] public partial class First
            {
                private First() {}
                public int Id { get; set; }
            }
            [MapFrom(typeof(Source))] public partial class Second
            {
                private Second(int version = 7) { Version = version; }
                [MapIgnore] public int Version { get; }
                public int Id { get; init; }
            }
            public static class Probe { public static bool Run() =>
                First.From(new Source()).Id == 42 && Second.From(new Source()).Version == 7; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("public required int Missing { get; init; }")]
    [InlineData("[MapIgnore] public required int Id { get; init; }")]
    [InlineData("public required int Id;")]
    public void Rejects_unfulfilled_required_properties_and_fields(string member)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(Source))] public partial class Destination { {{member}} }
            """);
        ContractTestAssertions.Rejects(result, "MFG007");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Supports_mapped_required_and_trusts_sets_required_members_constructors()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            using System.Diagnostics.CodeAnalysis;
            public class Source { public int Id => 7; }
            public class Base { public required int Id { get; init; } }
            [MapFrom(typeof(Source))] public partial class First : Base;
            [MapFrom(typeof(Source))] public partial class Second
            {
                [MapIgnore] public required int Id { get; init; }
                [SetsRequiredMembers] private Second() { Id = 42; }
            }
            public static class Probe { public static bool Run() =>
                First.From(new Source()).Id == 7 && Second.From(new Source()).Id == 42; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Does_not_emit_calls_to_rejected_nested_maps()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Child;
            [MapFrom(typeof(Child))] public abstract partial class ChildDto;
            public class Source { public Child Value { get; set; } = new(); }
            [MapFrom(typeof(Source))] public partial class Destination { public ChildDto Value { get; set; } = null!; }
            """);
        ContractTestAssertions.Rejects(result, "MFG006");
        ContractTestAssertions.Rejects(result, "MFG003");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Distinguishes_colliding_From_signatures_from_compatible_existing_and_inherited_overloads()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public int Id => 42; }
            public class Other;
            [MapFrom(typeof(Source))] public partial class Base { public int Id { get; set; } }
            [MapFrom(typeof(Source))] public partial class Derived : Base
            {
                public static Derived From(Other source) => new() { Id = 7 };
                public static Derived From<T>(Source source) => new() { Id = -1 };
            }
            public static class Probe { public static bool Run() =>
                Base.From(new Source()).Id == 42 && Derived.From(new Source()).Id == 42 && Derived.From(new Other()).Id == 7; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Resolves_private_constructor_overloads_declared_in_another_partial()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public int Id => 42; }
            [MapFrom(typeof(Source))] public partial class Destination { public int Id { get; set; } }
            public partial class Destination
            {
                private Destination() {}
                private Destination(int other = 0) {}
            }
            public static class Probe { public static int Run() => Destination.From(new Source()).Id; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<int>(result).Should().Be(42);
    }

    [Fact]
    public void Rejects_reserved_helper_name_before_generating_conflicting_code()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source;
            [MapFrom(typeof(Source))] public partial class Destination;
            namespace MapperForge { public class MapperForgeGeneratedExtensions; }
            """);
        ContractTestAssertions.Rejects(result, "MFG006");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_source_signature_that_collides_with_a_generated_collection_overload()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            using System.Collections.Generic;
            public class Source;
            [MapFrom(typeof(Source))] public partial class ElementDto;
            [MapFrom(typeof(IEnumerable<Source>))] public partial class CollectionDto;
            """);
        ContractTestAssertions.Rejects(result, "MFG006");
        result.GeneratedSources.Should().HaveCount(2);
    }
}
