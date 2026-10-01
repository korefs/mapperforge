using FluentAssertions;

namespace MapperForge.Generator.Tests;

public sealed class TransformContractTests
{
    [Theory]
    [InlineData("private string Convert(string value) => value;")]
    [InlineData("private static string Convert<T>(string value) => value;")]
    [InlineData("private static void Convert(string value) {}")]
    [InlineData("private static string Convert(ref string value) => value;")]
    [InlineData("private static string Convert(in string value) => value;")]
    [InlineData("private static string Convert(out string value) { value = \"\"; return value; }")]
    [InlineData("private static string Convert(string value, int other = 0) => value;")]
    [InlineData("private static string Convert() => \"\";")]
    [InlineData("private static string Convert(int value) => \"\";")]
    [InlineData("private static int Convert(string value) => 42;")]
    public void Rejects_invalid_transform_signatures(string method)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Source { public string Value => "input"; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                [MapTransform("Convert")] public string Value { get; init; } = "";
                {{method}}
            }
            """);
        ContractTestAssertions.Rejects(result, "MFG004");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("string", "\"input\"",
        "private static string Convert(object value) => \"object\"; private static string Convert(string value) => \"exact\";", "exact")]
    [InlineData("byte", "7",
        "private static string Convert(short value) => \"signed\"; private static string Convert(ushort value) => \"unsigned\";", "signed")]
    [InlineData("byte", "7",
        "private static string Convert(short value) => \"signed\"; private static string Convert(uint value) => \"unsigned\";", "signed")]
    [InlineData("ushort", "7",
        "private static string Convert(int value) => \"signed\"; private static string Convert(ulong value) => \"unsigned\";", "signed")]
    [InlineData("int", "7",
        "private static string Convert(double value) => \"double\"; private static string Convert(long value) => \"long\";", "long")]
    [InlineData("byte?", "7",
        "private static string Convert(short? value) => \"signed\"; private static string Convert(ushort? value) => \"unsigned\";", "signed")]
    [InlineData("System.Collections.Generic.List<int>", "new()",
        "private static string Convert(System.Collections.Generic.IEnumerable<int> value) => \"enumerable\"; private static string Convert(System.Collections.Generic.ICollection<int> value) => \"collection\";", "collection")]
    public void Selects_the_best_implicit_input_conversion(string inputType, string initialValue, string methods, string expected)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;
            public class Source { public {{inputType}} Value { get; set; } = {{initialValue}}; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                [MapTransform("Convert")] public string Value { get; init; } = "";
                {{methods}}
                public static string Expected({{inputType}} value) => Convert(value);
            }
            public static class Probe
            {
                public static string Run()
                {
                    var source = new Source();
                    var mapped = Destination.From(source).Value;
                    if (mapped != Destination.Expected(source.Value))
                        throw new System.InvalidOperationException("Generated overload differs from the C# compiler's overload resolution");
                    return mapped;
                }
            }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<string>(result).Should().Be(expected);
    }

    [Fact]
    public void Reports_ambiguous_overloads_without_choosing_declaration_order()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public interface IFirst;
            public interface ISecond;
            public class Both : IFirst, ISecond;
            public class Source { public Both Value { get; set; } = new(); }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                [MapTransform("Convert")] public string Value { get; init; } = "";
                private static string Convert(IFirst value) => "first";
                private static string Convert(ISecond value) => "second";
            }
            """);
        ContractTestAssertions.Rejects(result, "MFG004");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void Accepts_implicit_return_conversion_and_propagates_transform_exceptions()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            using System;
            public class Source { public int Value => 42; }
            [MapFrom(typeof(Source))] public partial class Good
            {
                [MapTransform("Convert")] public long Value { get; init; }
                private static int Convert(long value) => (int)value;
            }
            [MapFrom(typeof(Source))] public partial class Throwing
            {
                [MapTransform("Convert")] public int Value { get; init; }
                private static int Convert(int value) => throw new InvalidOperationException("from transform");
            }
            public static class Probe
            {
                public static bool Run()
                {
                    if (Good.From(new Source()).Value != 42) return false;
                    try { Throwing.From(new Source()); return false; }
                    catch (InvalidOperationException exception) { return exception.Message == "from transform"; }
                }
            }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<bool>(result).Should().BeTrue();
    }

    [Fact]
    public void Rejects_inaccessible_inherited_transform()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public string Value => "input"; }
            public class Base { private static string Convert(string value) => value; }
            [MapFrom(typeof(Source))] public partial class Destination : Base
            {
                [MapTransform("Convert")] public string Value { get; set; } = "";
            }
            """);
        ContractTestAssertions.Rejects(result, "MFG004");
    }

    [Fact]
    public void Reads_a_ref_returning_ordinary_transform_as_a_value()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;
            public class Source { public int Value => 7; }
            [MapFrom(typeof(Source))] public partial class Destination
            {
                [MapTransform("Convert")] public int Value { get; set; }
                private static int _value = 42;
                private static ref int Convert(int value) => ref _value;
            }
            public static class Probe { public static int Run() => Destination.From(new Source()).Value; }
            """);
        ContractTestAssertions.Compiles(result);
        GeneratorTestHost.Execute<int>(result).Should().Be(42);
    }
}
