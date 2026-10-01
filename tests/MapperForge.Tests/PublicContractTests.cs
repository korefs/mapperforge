using System.Collections;
using FluentAssertions;

namespace MapperForge.Tests;

public sealed class PublicContractTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Unknown_collection_destination_fails_before_enumeration(bool alias, bool empty)
    {
        var source = new TrackingEnumerable(empty);
        var action = () => alias ? source.MapTo<UnknownDto>() : source.MapToList<UnknownDto>();

        action.Should().Throw<MapperForgeMappingException>().WithMessage("*could not find a generated mapping*");
        source.Enumerations.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Valid_collection_destination_enumerates_once_including_empty_sources(bool empty)
    {
        var source = new TrackingEnumerable(empty);
        var result = source.MapToList<UserDto>();

        result.Should().HaveCount(empty ? 0 : 1);
        source.Enumerations.Should().Be(1);
        if (!empty) result[0].Id.Should().Be(42);
    }

    [Fact]
    public void Every_reference_root_api_rejects_null_with_argument_null_exception()
    {
        var from = () => UserDto.From(null!);
        var map = () => ((User)null!).MapTo<UserDto>();
        var list = () => ((IEnumerable<User>)null!).MapToList<UserDto>();
        var alias = () => ((IEnumerable<User>)null!).MapTo<UserDto>();
        var fallback = () => ((object)null!).MapTo<UserDto>();

        foreach (var action in new Action[] { () => from(), () => map(), () => list(), () => alias(), () => fallback() })
            action.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("source");
    }

    [Fact]
    public void Object_typed_source_keeps_explicit_failure_even_if_runtime_type_has_a_map()
    {
        object source = new User(42, "Ada", "ADA@EXAMPLE.COM");
        var action = () => source.MapTo<UserDto>();

        action.Should().Throw<MapperForgeMappingException>();
    }

    private sealed class TrackingEnumerable(bool empty) : IEnumerable<User>
    {
        public int Enumerations { get; private set; }

        public IEnumerator<User> GetEnumerator()
        {
            Enumerations++;
            if (Enumerations > 1) throw new InvalidOperationException("Source enumerated twice");
            return (empty ? Array.Empty<User>() : new[] { new User(42, "Ada", "ADA@EXAMPLE.COM") })
                .AsEnumerable().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
