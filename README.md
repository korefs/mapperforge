# MapperForge

[![CI](https://github.com/your-org/mapperforge/actions/workflows/ci.yml/badge.svg)](https://github.com/your-org/mapperforge/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MapperForge.svg)](https://www.nuget.org/packages/MapperForge)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)

MapperForge is an experimental .NET object mapper built around Roslyn source generation. It generates explicit C# mapping code at compile time, avoids runtime reflection, and keeps mapping behavior easy to debug.

## Install

```bash
dotnet add package MapperForge
```

The NuGet package contains both the runtime attributes and the source generator analyzer.

## Why MapperForge

- Compile-time generated mapping code.
- No runtime reflection for generated mappings.
- Familiar APIs for DTOs, commands, view models, and entities.
- Diagnostics that explain unmapped, incompatible, or invalid members while you build.
- Good fit for Clean Architecture, DDD, ASP.NET Core APIs, and corporate codebases where predictable code matters.

## Basic Usage

```csharp
using MapperForge;

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

var dto = UserDto.From(user);
var dto2 = user.MapTo<UserDto>();
var list = users.MapToList<UserDto>();
var sameList = users.MapTo<UserDto>();
```

Destination types must be `partial` because MapperForge adds the generated `From` method to them.

## Rename Members With MapProperty

```csharp
[MapFrom(typeof(User))]
public partial class UserDto
{
    [MapProperty(nameof(User.DisplayName))]
    public string Name { get; init; } = "";
}
```

## Ignore Members With MapIgnore

```csharp
[MapFrom(typeof(User))]
public partial class UserDto
{
    public int Id { get; init; }

    [MapIgnore]
    public string InternalNote { get; init; } = "created locally";
}
```

## Transform Members With MapTransform

```csharp
[MapFrom(typeof(User))]
public partial class UserDto
{
    [MapTransform(nameof(NormalizeEmail))]
    public string Email { get; init; } = "";

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
```

Transform methods must be static, visible to the generated partial type, accept the resolved source member type, and return a value assignable to the destination member.

## Nested Mapping

MapperForge maps nested objects when the nested destination type also declares an explicit source mapping:

```csharp
public sealed class User
{
    public Address Address { get; set; } = new();
}

[MapFrom(typeof(User))]
public partial class UserDto
{
    public AddressDto Address { get; set; } = new();
}

[MapFrom(typeof(Address))]
public partial class AddressDto
{
    public string Street { get; set; } = "";
}
```

The generated assignment is equivalent to:

```csharp
Address = AddressDto.From(source.Address)
```

Nullable nested mappings preserve nulls:

```csharp
public Address? BillingAddress { get; set; }
public AddressDto? BillingAddress { get; set; }
```

Collections are supported for `List<T>` and `IReadOnlyList<T>` destinations when the element mapping exists:

```csharp
public List<Address> PreviousAddresses { get; set; } = [];
public List<AddressDto> PreviousAddresses { get; set; } = [];

public IReadOnlyList<Address> KnownAddresses { get; set; } = [];
public IReadOnlyList<AddressDto> KnownAddresses { get; set; } = [];
```

## Diagnostics

| ID | Severity | Meaning |
| --- | --- | --- |
| MFG001 | Error | Destination type with `[MapFrom]` is not partial. |
| MFG002 | Warning | Destination member could not be mapped from the source type. |
| MFG003 | Error | Source and destination member types are incompatible. |
| MFG004 | Error | Transform method was not found or has an invalid signature. |
| MFG005 | Error | Destination member has no accessible setter. |

## Performance

MapperForge emits plain C# assignments and static method calls. The benchmark project compares manual mapping with MapperForge generated mapping:

```bash
dotnet run -c Release --project benchmarks/MapperForge.Benchmarks
```

Benchmark baselines for AutoMapper and Mapster are intentionally left for a later milestone so the first version stays focused and transparent.

## Roadmap

- Constructor and primary-constructor mapping.
- Records and immutable model enhancements.
- Collection conversion beyond `List<T>` and `IReadOnlyList<T>` nested mapping.
- Global conventions and configuration.
- Dependency injection helpers.
- AutoMapper and Mapster benchmark baselines.
- Analyzer release tracking and code fixes.

## Status

MapperForge is experimental. The MVP API is intentionally small while the generator behavior, diagnostics, and package shape stabilize.

## License

MapperForge is licensed under the [MIT License](LICENSE).
