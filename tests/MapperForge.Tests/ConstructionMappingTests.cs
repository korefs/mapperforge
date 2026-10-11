using System.Diagnostics.CodeAnalysis;
using FluentAssertions;

namespace MapperForge.Tests;

public sealed class ConstructionMappingTests
{
    [Fact]
    public void Maps_positional_records_through_all_public_entry_points()
    {
        var source = new User(42, "Ada", " ADA@EXAMPLE.COM ");
        ImmutableUserDto.From(source).Should().Be(new ImmutableUserDto(42, "Ada", "ada@example.com"));
        source.MapTo<ImmutableUserDto>().Should().Be(new ImmutableUserDto(42, "Ada", "ada@example.com"));
        new[] { source }.MapToList<ImmutableUserDto>().Should().Equal(new ImmutableUserDto(42, "Ada", "ada@example.com"));
        new[] { source }.MapTo<ImmutableUserDto>().Should().Equal(new ImmutableUserDto(42, "Ada", "ada@example.com"));
    }

    [Fact]
    public void Maps_immutable_nested_arguments_and_nullable_collections()
    {
        var profile = new Profile
        {
            Address = new() { Street = "Main" },
            BillingAddress = new() { Street = "Billing" },
            PreviousAddresses = [new() { Street = "Previous" }],
            KnownAddresses = [new() { Street = "Known" }]
        };
        var dto = profile.MapTo<ImmutableProfileDto>();
        dto.Address.Street.Should().Be("Main");
        dto.BillingAddress!.Street.Should().Be("Billing");
        dto.PreviousAddresses.Should().Equal(new ImmutableAddressDto("Previous"));
        dto.KnownAddresses.Should().Equal(new ImmutableAddressDto("Known"));
        ImmutableProfileDto.From(new Profile()).BillingAddress.Should().BeNull();
    }

    [Fact]
    public void Construction_satisfies_required_without_repeating_the_setter()
    {
        var dto = new User(42, "Ada", "ada@example.com").MapTo<RequiredUserDto>();
        dto.Id.Should().Be(42);
        dto.Name.Should().Be("Ada");
        dto.Calls.Should().Be(1);
    }

    [Fact]
    public void Positional_from_rejects_null_and_propagates_transform_exceptions()
    {
        var nullSource = () => ImmutableUserDto.From(null!);
        nullSource.Should().Throw<ArgumentNullException>();
        var invalidEmail = () => ImmutableUserDto.From(new User(1, "Ada", ""));
        invalidEmail.Should().Throw<ArgumentException>();
    }
}

[MapFrom(typeof(User))]
public partial record ImmutableUserDto(
    int Id,
    [property: MapProperty(nameof(User.DisplayName))] string Name,
    [property: MapTransform("Normalize")] string Email)
{
    private static string Normalize(string email) => string.IsNullOrWhiteSpace(email)
        ? throw new ArgumentException("Email is empty.") : email.Trim().ToLowerInvariant();
}

[MapFrom(typeof(Address))]
public partial record ImmutableAddressDto(string Street);

[MapFrom(typeof(Profile))]
public partial record ImmutableProfileDto(
    ImmutableAddressDto Address,
    ImmutableAddressDto? BillingAddress,
    List<ImmutableAddressDto> PreviousAddresses,
    IReadOnlyList<ImmutableAddressDto> KnownAddresses);

[MapFrom(typeof(User))]
public partial class RequiredUserDto
{
    private int id;
    [SetsRequiredMembers]
    private RequiredUserDto(int id) { Id = id; }
    public required int Id { get => id; init { id = value; Calls++; } }
    [MapProperty(nameof(User.DisplayName))] public string Name { get; init; } = "";
    [MapIgnore] public int Calls { get; private set; }
}
