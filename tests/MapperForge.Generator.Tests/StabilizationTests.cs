using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Tests;

public sealed class StabilizationTests
{
    private static void AssertCompiles(GeneratorRunResult result)
    {
        result.Diagnostics.Should().BeEmpty();
        result.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void Maps_inherited_properties_and_attributes_and_prefers_derived_declarations()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class SourceBase
            {
                public int Id { get; set; }
                public virtual string Name => "base";
                public string Value => "wrong type";
                public string Alias => "renamed";
                public int Score => 7;
            }
            public class Source : SourceBase
            {
                public override string Name => "derived";
                public new int Value => 99;
                public string Label => "fresh";
                public string Transformed => "fresh";
                public string Ignored => "fresh";
            }
            public class DestinationBase
            {
                public int Id { get; protected set; }
                [MapProperty("Alias")] public virtual string Name { get; set; } = "";
                public string Value { get; set; } = "base";
                [MapProperty("Alias")] public string Renamed { get; set; } = "";
                [MapIgnore] public string Note { get; set; } = "kept";
                [MapTransform("Double")] public int Score { get; set; }
                [MapProperty("Alias")] public virtual string Label { get; set; } = "";
                [MapTransform("Decorate")] public virtual string Transformed { get; set; } = "";
                [MapIgnore] public virtual string Ignored { get; set; } = "";
                protected static int Double(int value) => value * 2;
            }
            [MapFrom(typeof(Source))]
            public partial class Destination : DestinationBase
            {
                public override string Name { get; set; } = "";
                public new int Value { get; private set; }
                public new string Label { get; set; } = "";
                public override string Transformed { get; set; } = "";
                public override string Ignored { get; set; } = "";
                private static string Decorate(string value) => "wrong";
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var dto = Destination.From(new Source { Id = 42 });
                    return dto.Id == 42 && dto.Name == "derived" && dto.Value == 99 &&
                        ((DestinationBase)dto).Value == "base" && dto.Renamed == "renamed" &&
                        dto.Note == "kept" && dto.Score == 14 && dto.Label == "fresh" &&
                        dto.Transformed == "fresh" && dto.Ignored == "fresh";
                }
            }
            """);
        AssertCompiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Rejects_private_base_setter_without_emitting_broken_mapping()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public int Id { get; set; } }
            public class Base { public int Id { get; private set; } }
            [MapFrom(typeof(Source))] public partial class Destination : Base;
            """);
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "MFG005");
        result.GeneratedSources.Should().BeEmpty();
        result.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("internal", false)]
    [InlineData("private protected", false)]
    [InlineData("protected", true)]
    [InlineData("protected internal", true)]
    public void Evaluates_inherited_setter_accessibility_in_the_destination_assembly(string accessibility, bool accessible)
    {
        var reference = GeneratorTestHost.CreateReference(
            $$"""public class Base { public int Id { get; {{accessibility}} set; } }""");
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public int Id { get; set; } }
            [MapFrom(typeof(Source))] public partial class Destination : Base;
            """, reference);
        if (accessible)
        {
            AssertCompiles(result);
            result.GeneratedSources.Should().NotBeEmpty();
        }
        else
        {
            result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "MFG005");
            result.GeneratedSources.Should().BeEmpty();
            result.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Should().BeEmpty();
        }
    }

    [Fact]
    public void Does_not_fall_back_to_base_property_when_derived_getter_is_private()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Base { public int Id => 42; }
            public class Source : Base { public new int Id { private get; set; } }
            [MapFrom(typeof(Source))] public partial class Destination { public int Id { get; set; } }
            public static class Probe { public static int Run() => Destination.From(new Source { Id = 7 }).Id; }
            """);
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "MFG002");
        result.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        GeneratorTestHost.Execute<int>(result).Should().Be(0);
    }

    [Fact]
    public void Supports_multiple_sources_and_destinations_and_deduplicates_across_partial_declarations()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class First { public int Id { get; set; } }
            public class Second { public int Id { get; set; } }
            [MapFrom(typeof(First)), MapFrom(typeof(Second)), MapFrom(typeof(First))]
            public partial class Destination { public int Id { get; set; } }
            [MapFrom(typeof(Second))] public partial class Destination;
            [MapFrom(typeof(First))] public partial class Other { public int Id { get; set; } }
            public static class Probe
            {
                public static bool Run()
                {
                    var first = new First { Id = 42 };
                    var second = new Second { Id = 7 };
                    return Destination.From(first).Id == 42 && Destination.From(second).Id == 7 &&
                        first.MapTo<Destination>().Id == 42 && second.MapTo<Destination>().Id == 7 &&
                        first.MapTo<Other>().Id == 42 && new[] { first }.MapToList<Other>()[0].Id == 42 &&
                        new[] { second }.MapTo<Destination>()[0].Id == 7;
                }
            }
            """);
        AssertCompiles(result);
        result.GeneratedSources.Should().HaveCount(4);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Distinguishes_closed_generic_sources_in_overloads_nested_maps_and_collections()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            using System.Collections.Generic;
            public class Source<T> { public T Value { get; set; } = default!; }
            [MapFrom(typeof(Source<int>)), MapFrom(typeof(Source<string>))]
            public partial class Destination { public object Value { get; set; } = null!; }
            public class Parent
            {
                public Source<int> Number { get; set; } = new() { Value = 42 };
                public Source<string> Text { get; set; } = new() { Value = "text" };
                public List<Source<int>> Numbers { get; set; } = new() { new() { Value = 7 } };
                public List<Source<string>> Texts { get; set; } = new() { new() { Value = "list" } };
            }
            [MapFrom(typeof(Parent))]
            public partial class ParentDto
            {
                public Destination Number { get; set; } = new();
                public Destination Text { get; set; } = new();
                public List<Destination> Numbers { get; set; } = new();
                public List<Destination> Texts { get; set; } = new();
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var dto = ParentDto.From(new Parent());
                    return (int)dto.Number.Value == 42 && (string)dto.Text.Value == "text" &&
                        (int)dto.Numbers[0].Value == 7 && (string)dto.Texts[0].Value == "list" &&
                        (int)new Source<int> { Value = 9 }.MapTo<Destination>().Value == 9 &&
                        (string)new Source<string> { Value = "direct" }.MapTo<Destination>().Value == "direct";
                }
            }
            """);
        AssertCompiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("Source<string>", "Destination")]
    [InlineData("System.Collections.Generic.List<Source<string>>", "System.Collections.Generic.List<Destination>")]
    public void Does_not_reuse_a_map_for_another_generic_construction(string source, string destination)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Source<T> { public T Value { get; set; } = default!; }
            [MapFrom(typeof(Source<int>))] public partial class Destination { public int Value { get; set; } }
            public class Parent { public {{source}} Child { get; set; } = new(); }
            [MapFrom(typeof(Parent))] public partial class ParentDto { public {{destination}} Child { get; set; } = new(); }
            """);
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "MFG003");
        result.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        result.GeneratedSources.Should().HaveCount(2, "the independent int mapping remains valid");
    }

    [Fact]
    public void Hint_names_are_stable_and_do_not_collide_after_sanitization()
    {
        const string source = """
            using MapperForge;
            namespace A_B { public class Source { public int Id => 42; } }
            namespace A.B { public class Source { public int Id => 7; } }
            [MapFrom(typeof(A_B.Source)), MapFrom(typeof(A.B.Source))]
            public partial class Destination { public int Id { get; set; } }
            public static class Probe
            {
                public static bool Run() => Destination.From(new A_B.Source()).Id == 42 &&
                    Destination.From(new A.B.Source()).Id == 7;
            }
            """;
        var first = GeneratorTestHost.Run(source);
        var second = GeneratorTestHost.Run(source.Replace(
            "typeof(A_B.Source)), MapFrom(typeof(A.B.Source)",
            "typeof(A.B.Source)), MapFrom(typeof(A_B.Source)", StringComparison.Ordinal));
        AssertCompiles(first);
        AssertCompiles(second);
        first.GeneratedHintNames.Should().OnlyHaveUniqueItems().And.Equal(second.GeneratedHintNames);
        first.GeneratedSources.Should().Equal(second.GeneratedSources);
        GeneratorTestHost.Execute<bool>(first).Should().BeTrue();
    }

    [Fact]
    public void Generic_argument_names_that_sanitize_identically_keep_both_maps()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class A_B;
            public class A { public class B; }
            public class Source<T> { public int Id { get; set; } }
            [MapFrom(typeof(Source<A_B>)), MapFrom(typeof(Source<A.B>))]
            public partial class Destination { public int Id { get; set; } }
            public static class Probe
            {
                public static bool Run() => Destination.From(new Source<A_B> { Id = 42 }).Id == 42 &&
                    Destination.From(new Source<A.B> { Id = 7 }).Id == 7;
            }
            """);
        AssertCompiles(result);
        result.GeneratedHintNames.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Maps_inherited_interface_members_and_compatible_diamonds()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public interface IRoot { int Id { get; } }
            public interface ILeft : IRoot { string Name { get; } }
            public interface IRight : IRoot { string Name { get; } }
            public interface ISource : ILeft, IRight;
            public class Source : ISource
            {
                public int Id => 42;
                string ILeft.Name => "left";
                string IRight.Name => "right";
            }
            [MapFrom(typeof(ISource))]
            public partial class Destination
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var dto = Destination.From(new Source());
                    return dto.Id == 42 && dto.Name == "left";
                }
            }
            """);
        AssertCompiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Prefers_redeclared_interface_property_over_incompatible_ancestors()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public interface ILeft { int Id { get; } }
            public interface IRight { string Id { get; } }
            public interface ISource : ILeft, IRight { new int Id { get; } }
            public class Source : ISource
            {
                public int Id => 42;
                string IRight.Id => "base";
            }
            [MapFrom(typeof(ISource))] public partial class Destination { public int Id { get; set; } }
            public static class Probe { public static int Run() => Destination.From(new Source()).Id; }
            """);
        AssertCompiles(result);
        GeneratorTestHost.Execute<int>(result).Should().Be(42);
    }

    [Fact]
    public void Reports_incompatible_inherited_interface_properties_without_generator_failure()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public interface ILeft { int Id { get; } }
            public interface IRight { string Id { get; } }
            public interface ISource : ILeft, IRight;
            [MapFrom(typeof(ISource))] public partial class Destination { public int Id { get; set; } }
            """);
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MFG012" && diagnostic.Severity == DiagnosticSeverity.Error);
        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8785");
        result.GeneratedSources.Should().BeEmpty();
        result.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }
}
