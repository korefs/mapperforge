using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Tests;

public sealed class MapperForgeGeneratorTests
{
    [Fact]
    public void Generates_compilable_mapping_without_reflection()
    {
        var result = GeneratorTestHost.Run(
            """
            using MapperForge;

            namespace Demo;

            public sealed class User
            {
                public int Id { get; init; }
                public string Name { get; init; } = "";
            }

            [MapFrom(typeof(User))]
            public partial class UserDto
            {
                public int Id { get; init; }
                public string Name { get; init; } = "";
            }
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        result.Compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var generated = string.Join(Environment.NewLine, result.GeneratedSources);
        generated.Should().Contain("UserDto.From(source)");
        generated.Should().Contain("internal static global::System.Collections.Generic.List<TDestination> MapTo<TDestination>(this global::System.Collections.Generic.IEnumerable<global::Demo.User> source)");
        generated.Should().MatchRegex(@"return global::MapperForge.MapperForgeGeneratedExtensions_[a-f0-9]{64}\.MapToList<TDestination>\(source\);");
        generated.Should().NotContain("System.Reflection");
        generated.Should().NotContain("GetProperty");
        generated.Should().NotContain("Invoke");
    }

    [Fact]
    public void Generates_nested_object_and_collection_mappings()
    {
        var result = GeneratorTestHost.Run(
            """
            using System.Collections.Generic;
            using MapperForge;

            namespace Demo;

            public sealed class User
            {
                public Address Address { get; set; } = new();
                public Address? BillingAddress { get; set; }
                public List<Address> PreviousAddresses { get; set; } = new();
                public IReadOnlyList<Address> KnownAddresses { get; set; } = new List<Address>();
            }

            public sealed class Address
            {
                public string Street { get; set; } = "";
            }

            [MapFrom(typeof(User))]
            public partial class UserDto
            {
                public AddressDto Address { get; set; } = new();
                public AddressDto? BillingAddress { get; set; }
                public List<AddressDto> PreviousAddresses { get; set; } = new();
                public IReadOnlyList<AddressDto> KnownAddresses { get; set; } = new List<AddressDto>();
            }

            [MapFrom(typeof(Address))]
            public partial class AddressDto
            {
                public string Street { get; set; } = "";
            }
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        result.Compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var generated = string.Join(Environment.NewLine, result.GeneratedSources);
        generated.Should().Contain("Address = global::Demo.AddressDto.From(source.Address)");
        generated.Should().Contain("BillingAddress = source.BillingAddress is { } __mfgValue1 ? global::Demo.AddressDto.From(__mfgValue1) : null");
        generated.Should().MatchRegex(@"PreviousAddresses = global::MapperForge.MapperForgeGeneratedExtensions_[a-f0-9]{64}\.MapToList<global::Demo.AddressDto>\(source.PreviousAddresses\)");
        generated.Should().MatchRegex(@"KnownAddresses = global::MapperForge.MapperForgeGeneratedExtensions_[a-f0-9]{64}\.MapToList<global::Demo.AddressDto>\(source.KnownAddresses\)");
    }

    [Theory]
    [InlineData("MFG001", DiagnosticSeverity.Error, "public class UserDto")]
    [InlineData("MFG002", DiagnosticSeverity.Warning, "public partial class UserDto { public string Missing { get; set; } = \"\"; }")]
    [InlineData("MFG003", DiagnosticSeverity.Error, "public partial class UserDto { public int Name { get; set; } }")]
    [InlineData("MFG004", DiagnosticSeverity.Error, "public partial class UserDto { [MapTransform(nameof(Missing))] public string Name { get; set; } = \"\"; }")]
    [InlineData("MFG005", DiagnosticSeverity.Error, "public partial class UserDto { public string Name { get; } = \"\"; }")]
    public void Reports_expected_diagnostics(string diagnosticId, DiagnosticSeverity severity, string destinationDeclaration)
    {
        var result = GeneratorTestHost.Run(
            $$"""
            using MapperForge;

            namespace Demo;

            public sealed class User
            {
                public string Name { get; init; } = "";
            }

            [MapFrom(typeof(User))]
            {{destinationDeclaration}}
            """);

        result.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == diagnosticId &&
            diagnostic.Severity == severity);
    }
}
