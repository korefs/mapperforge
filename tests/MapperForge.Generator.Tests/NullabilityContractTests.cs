using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Tests;

public sealed class NullabilityContractTests
{
    [Theory]
    [InlineData("string?", "string")]
    [InlineData("int?", "int")]
    [InlineData("System.Collections.Generic.List<string?>", "System.Collections.Generic.List<string>")]
    [InlineData("System.Collections.Generic.List<string?>", "System.Collections.Generic.IReadOnlyList<string>")]
    [InlineData("string?[]", "string[]")]
    public void Rejects_nullable_values_and_direct_collection_elements_for_non_nullable_destinations(string from, string to)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Source { public {{from}} Value { get; set; } = default!; }
            [MapFrom(typeof(Source))] public partial class Destination { public {{to}} Value { get; set; } = default!; }
            """);
        ContractTestAssertions.Rejects(result, "MFG008");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("string", "string", "\"value\"")]
    [InlineData("string", "string?", "\"value\"")]
    [InlineData("string?", "string?", "null")]
    [InlineData("int", "int?", "42")]
    [InlineData("int?", "int?", "null")]
    [InlineData("System.Collections.Generic.List<string>", "System.Collections.Generic.List<string?>", "new() { \"value\" }")]
    [InlineData("System.Collections.Generic.List<string>", "System.Collections.Generic.IReadOnlyList<string?>", "new() { \"value\" }")]
    public void Allows_safe_direct_nullability_conversions(string from, string to, string value)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Source { public {{from}} Value { get; set; } = {{value}}; }
            [MapFrom(typeof(Source))] public partial class Destination { public {{to}} Value { get; set; } = default!; }
            public static class Probe
            {
                public static bool Run()
                {
                    var source = new Source();
                    var dto = Destination.From(source);
                    return object.Equals(source.Value, dto.Value);
                }
            }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    public static IEnumerable<object[]> NestedCases()
    {
        foreach (var kind in new[] { "class", "struct" })
            foreach (var sourceNullable in new[] { false, true })
                foreach (var destinationNullable in new[] { false, true })
                    yield return [kind, sourceNullable, destinationNullable];
    }

    [Theory]
    [MemberData(nameof(NestedCases))]
    public void Applies_null_policy_to_nested_reference_and_value_maps(string kind, bool sourceNullable, bool destinationNullable)
    {
        var accepted = !sourceNullable || destinationNullable;
        var probe = !accepted ? "" : $$"""
            public static class Probe
            {
                public static bool Run()
                {
                    var dto = Destination.From(new Source { Value = new Child { Id = 42 } });
                    if ({{(destinationNullable ? "dto.Value?.Id" : "dto.Value.Id")}} != 42) return false;
                    {{(sourceNullable ? "return Destination.From(new Source { Value = null }).Value is null;" : "return true;")}}
                }
            }
            """;
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public {{kind}} Child { public int Id { get; set; } }
            [MapFrom(typeof(Child))] public partial {{kind}} ChildDto { public int Id { get; set; } }
            public class Source { public Child{{(sourceNullable ? "?" : "")}} Value { get; set; } = new(); }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                public ChildDto{{(destinationNullable ? "?" : "")}} Value { get; set; } = new();
            }
            {{probe}}
            """);
        if (!accepted)
        {
            ContractTestAssertions.Rejects(result, "MFG008");
            return;
        }
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
        string.Join("\n", result.GeneratedSources).Should().NotContain("source.Value!");
    }

    public static IEnumerable<object[]> CollectionCases()
    {
        for (var flags = 0; flags < 16; flags++)
            yield return [(flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0];
    }

    [Theory]
    [MemberData(nameof(CollectionCases))]
    public void Applies_null_policy_to_collection_roots_and_elements(bool sourceNullable, bool destinationNullable,
        bool sourceElementNullable, bool destinationElementNullable)
    {
        var accepted = (!sourceNullable || destinationNullable) && (!sourceElementNullable || destinationElementNullable);
        var sourceElement = "Child" + (sourceElementNullable ? "?" : "");
        var destinationElement = "ChildDto" + (destinationElementNullable ? "?" : "");
        var probe = !accepted ? "" : $$"""
            public static class Probe
            {
                public static bool Run()
                {
                    var dto = Destination.From(new Source
                    {
                        Value = new List<{{sourceElement}}> { new Child { Id = 42 }{{(sourceElementNullable ? ", null" : "")}} }
                    });
                    if (dto.Value is null || {{(destinationElementNullable ? "dto.Value[0]?.Id" : "dto.Value[0].Id")}} != 42) return false;
                    {{(sourceElementNullable ? "if (dto.Value[1] is not null) return false;" : "")}}
                    {{(sourceNullable ? "return Destination.From(new Source { Value = null }).Value is null;" : "return true;")}}
                }
            }
            """;
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            using System.Collections.Generic;
            public class Child { public int Id { get; set; } }
            [MapFrom(typeof(Child))] public partial class ChildDto { public int Id { get; set; } }
            public class Source { public IEnumerable<{{sourceElement}}>{{(sourceNullable ? "?" : "")}} Value { get; set; } = new List<{{sourceElement}}>(); }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                public IReadOnlyList<{{destinationElement}}>{{(destinationNullable ? "?" : "")}} Value { get; set; } = new List<{{destinationElement}}>();
            }
            {{probe}}
            """);
        if (!accepted)
        {
            ContractTestAssertions.Rejects(result, "MFG008");
            return;
        }
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    public static IEnumerable<object[]> TransformCases() => CollectionCases();

    [Theory]
    [MemberData(nameof(TransformCases))]
    public void Applies_null_policy_to_transform_inputs_and_outputs(bool sourceNullable, bool destinationNullable,
        bool parameterNullable, bool returnNullable)
    {
        var accepted = (!sourceNullable || destinationNullable || parameterNullable) && (!returnNullable || destinationNullable);
        var nullExpected = parameterNullable && !returnNullable ? "\"fallback\"" : "null";
        var probe = !accepted ? "" : $$"""
            public static class Probe
            {
                public static bool Run()
                {
                    if (Destination.From(new Source { Value = "input" }).Value != "mapped") return false;
                    {{(sourceNullable ? "return Destination.From(new Source { Value = null }).Value == " + nullExpected + ";" : "return true;")}}
                }
            }
            """;
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Source { public string{{(sourceNullable ? "?" : "")}} Value { get; set; } = "input"; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                [MapTransform("Convert")] public string{{(destinationNullable ? "?" : "")}} Value { get; set; } = "";
                private static string{{(returnNullable ? "?" : "")}} Convert(string{{(parameterNullable ? "?" : "")}} value) =>
                    {{(parameterNullable ? "value is null ? " + nullExpected + " : \"mapped\"" : "\"mapped\"")}};
            }
            {{probe}}
            """);
        if (!accepted)
        {
            ContractTestAssertions.Rejects(result, "MFG008");
            result.GeneratedSources.Should().BeEmpty();
            return;
        }
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Handles_nullable_value_transforms_and_prefers_exact_nullable_overload()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public int? Value { get; set; } }
            [MapFrom(typeof(Source))] public partial class First
            {
                [MapTransform("Convert")] public int Value { get; set; }
                private static int Convert(int? value) => value ?? 42;
                private static int Convert(int value) => -1;
            }
            [MapFrom(typeof(Source))] public partial class Second
            {
                [MapTransform("Convert")] public int? Value { get; set; }
                private static int Convert(int value) => value * 2;
            }
            public static class Probe
            {
                public static bool Run() => First.From(new Source()).Value == 42 &&
                    First.From(new Source { Value = 7 }).Value == 7 &&
                    Second.From(new Source()).Value is null &&
                    Second.From(new Source { Value = 7 }).Value == 14;
            }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Warns_about_oblivious_source_without_hiding_risk_or_adding_runtime_member_guards()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            #nullable disable
            public class Source { public string Value { get; set; } }
            #nullable enable
            [MapFrom(typeof(Source))] public partial class Destination { public string Value { get; set; } = ""; }
            public static class Probe { public static bool Run() => Destination.From(new Source()).Value is null; }
            """);
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "MFG013" && diagnostic.Severity == DiagnosticSeverity.Warning);
        ContractTestAssertions.CompilerIsClean(result);
        string.Join("\n", result.GeneratedSources).Should().Contain("Value = source.Value,").And.NotContain("source.Value!");
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Does_not_accept_oblivious_transform_as_explicit_null_handling()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public string? Value { get; set; } }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                [MapTransform("Convert")] public string Value { get; set; } = "";
                #nullable disable
                private static string Convert(string value) => value ?? "";
                #nullable enable
            }
            """);
        ContractTestAssertions.Rejects(result, "MFG008");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validates_null_annotations_in_generic_containing_types(bool unsafeConversion)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Outer<T> { public class Inner; }
            public class Source { public Outer<string{{(unsafeConversion ? "?" : "")}}>.Inner Value { get; set; } = new(); }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                public Outer<string{{(unsafeConversion ? "" : "?")}}>.Inner Value { get; set; } = new();
            }
            """);
        if (unsafeConversion) ContractTestAssertions.Rejects(result, "MFG008");
        else ContractTestAssertions.Compiles(result);
    }

    [Fact]
    public void Explicit_nullable_collection_transform_can_supply_non_nullable_collection()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            using System.Collections.Generic;
            using System.Linq;
            public class Child { public int Id { get; set; } }
            [MapFrom(typeof(Child))] public partial class ChildDto { public int Id { get; set; } }
            public class Source { public List<Child>? Value { get; set; } }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                [MapTransform("Convert")] public List<ChildDto> Value { get; set; } = new();
                private static List<ChildDto> Convert(List<Child>? value) => value?.Select(ChildDto.From).ToList() ?? new();
            }
            public static class Probe { public static bool Run() =>
                Destination.From(new Source()).Value.Count == 0 &&
                Destination.From(new Source { Value = new() { new() { Id = 42 } } }).Value[0].Id == 42; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Reports_unknown_nullability_imported_from_library_metadata()
    {
        var reference = GeneratorTestHost.CreateReference(
            """
            #nullable disable
            public class Source { public string Value { get; set; } }
            """);
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            [MapFrom(typeof(Source))] public partial class Destination { public string Value { get; set; } = ""; }
            """, reference);
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "MFG013");
        ContractTestAssertions.CompilerIsClean(result);
    }

    [Fact]
    public void Copies_nullable_value_to_oblivious_collection_destination_without_inventing_a_non_nullable_cast()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            using System.Collections.Generic;
            public class Source { public List<string>? Value { get; set; } }
            #nullable disable
            [MapFrom(typeof(Source))] public partial class Destination { public List<string> Value { get; set; } }
            #nullable enable
            public static class Probe { public static bool Run() => Destination.From(new Source()).Value is null; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }
}
