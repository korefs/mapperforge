using FluentAssertions;

namespace MapperForge.Tests;

public sealed class MappingTests
{
    [Fact]
    public void From_maps_matching_renamed_ignored_and_transformed_members()
    {
        var user = new User(42, "Ada Lovelace", "ADA@EXAMPLE.COM");

        var dto = UserDto.From(user);

        dto.Id.Should().Be(42);
        dto.Name.Should().Be("Ada Lovelace");
        dto.Email.Should().Be("ada@example.com");
        dto.InternalNote.Should().Be("kept");
    }

    [Fact]
    public void MapTo_uses_generated_extension_method()
    {
        var user = new User(7, "Grace Hopper", "GRACE@EXAMPLE.COM");

        var dto = user.MapTo<UserDto>();

        dto.Id.Should().Be(7);
        dto.Name.Should().Be("Grace Hopper");
        dto.Email.Should().Be("grace@example.com");
    }

    [Fact]
    public void MapToList_maps_each_source_item()
    {
        var users = new[]
        {
            new User(1, "A", "A@EXAMPLE.COM"),
            new User(2, "B", "B@EXAMPLE.COM"),
        };

        var dtos = users.MapToList<UserDto>();

        dtos.Should().HaveCount(2);
        dtos.Select(static dto => dto.Id).Should().Equal(1, 2);
    }

    [Fact]
    public void MapTo_maps_enumerables_by_redirecting_to_list_mapping()
    {
        var users = new List<User>
        {
            new(1, "A", "A@EXAMPLE.COM"),
            new(2, "B", "B@EXAMPLE.COM"),
        };

        var dtos = users.MapTo<UserDto>();

        dtos.Should().BeOfType<List<UserDto>>();
        dtos.Select(static dto => dto.Email).Should().Equal("a@example.com", "b@example.com");
    }

    [Fact]
    public void From_throws_for_null_source()
    {
        var act = () => UserDto.From(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Unsupported_destination_throws_clear_mapperforge_exception()
    {
        var user = new User(1, "Ada", "ADA@EXAMPLE.COM");

        var act = () => user.MapTo<UnknownDto>();

        act.Should().Throw<MapperForgeMappingException>()
            .WithMessage("*could not find a generated mapping*");
    }

    [Fact]
    public void From_maps_nested_objects_when_nested_map_exists()
    {
        var profile = new Profile
        {
            Address = new Address { Street = "Main St" },
            BillingAddress = new Address { Street = "Billing St" },
            PreviousAddresses = [new Address { Street = "Old St" }],
            KnownAddresses = [new Address { Street = "Known St" }],
        };

        var dto = ProfileDto.From(profile);

        dto.Address.Street.Should().Be("Main St");
        dto.BillingAddress!.Street.Should().Be("Billing St");
        dto.PreviousAddresses.Should().ContainSingle(address => address.Street == "Old St");
        dto.KnownAddresses.Should().ContainSingle(address => address.Street == "Known St");
    }

    [Fact]
    public void From_preserves_null_for_nullable_nested_objects()
    {
        var profile = new Profile
        {
            Address = new Address { Street = "Main St" },
            BillingAddress = null,
            PreviousAddresses = [],
            KnownAddresses = [],
        };

        var dto = ProfileDto.From(profile);

        dto.BillingAddress.Should().BeNull();
    }
}

public sealed class User
{
    public User(int id, string displayName, string email)
    {
        Id = id;
        DisplayName = displayName;
        Email = email;
    }

    public int Id { get; }

    public string DisplayName { get; }

    public string Email { get; }
}

[MapFrom(typeof(User))]
public partial class UserDto
{
    public int Id { get; init; }

    [MapProperty(nameof(User.DisplayName))]
    public string Name { get; init; } = "";

    [MapTransform(nameof(NormalizeEmail))]
    public string Email { get; init; } = "";

    [MapIgnore]
    public string InternalNote { get; init; } = "kept";

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}

public sealed class UnknownDto;

public sealed class Profile
{
    public Address Address { get; set; } = new();

    public Address? BillingAddress { get; set; }

    public List<Address> PreviousAddresses { get; set; } = [];

    public IReadOnlyList<Address> KnownAddresses { get; set; } = [];
}

public sealed class Address
{
    public string Street { get; set; } = "";
}

[MapFrom(typeof(Profile))]
public partial class ProfileDto
{
    public AddressDto Address { get; set; } = new();

    public AddressDto? BillingAddress { get; set; }

    public List<AddressDto> PreviousAddresses { get; set; } = [];

    public IReadOnlyList<AddressDto> KnownAddresses { get; set; } = [];
}

[MapFrom(typeof(Address))]
public partial class AddressDto
{
    public string Street { get; set; } = "";
}
