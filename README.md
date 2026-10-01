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

## Inheritance and Multiple Sources

MapperForge maps properties declared on source and destination base classes. Properties are processed in ordinal name order; the most derived declaration wins for both `override` and `new`. This fixes inherited values that earlier versions silently left at their defaults.

Attributes on a selected inherited property remain effective. A new declaration or override uses its own attributes: `MapProperty`, `MapIgnore`, and `MapTransform` are not inherited by overrides. Inherited transforms can call accessible static methods on the destination's base classes. Setters are checked from the destination's context: a private setter on the destination works, while a private setter on a base class produces MFG005. Ignore computed or intentionally unmapped properties with `[MapIgnore]`.

Source interfaces include inherited properties. A declaration on a derived interface takes precedence. Compatible declarations from unrelated interfaces are resolved by the declaring interface's ordinal fully qualified name and read through that interface; incompatible declarations produce MFG012 until a derived interface redeclares the property.

A destination can declare multiple sources, and a source can map to multiple destinations:

```csharp
[MapFrom(typeof(User))]
[MapFrom(typeof(ImportedUser))]
public partial class UserDto
{
    public int Id { get; init; }
}

var local = UserDto.From(user);
var imported = UserDto.From(importedUser);
var another = importedUser.MapTo<UserDto>();
```

Repeating the same source/destination pair is idempotent, including across partial declarations. Closed generic sources such as `Source<int>` and `Source<string>` remain distinct in direct, nested, and collection mappings. Generated filenames include a stable SHA-256 suffix to distinguish names that sanitize identically. Generic destinations and generic containing types are outside the current supported scope; their explicit rejection is planned in the next stage.

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
| MFG012 | Error | Inherited interface members have incompatible declarations without a prevailing declaration. |

## Local Validation and Packaging

```bash
dotnet build MapperForge.sln -c Release
dotnet test MapperForge.sln -c Release --no-build
python3 scripts/verify-package.py
```

The package check requires Python 3, a compatible .NET SDK, and NuGet access for build dependencies and any missing .NET 8 reference packs. It copies sources into a temporary workspace without `bin` or `obj`, packs from scratch, checks that exactly one generator DLL exists in `analyzers/dotnet/cs`, and executes a consumer referencing only the local package. It repeats consumption after build → pack `--no-build`, using separate empty NuGet package caches and source mapping that restricts MapperForge to the local package. It also verifies that a missing generator DLL causes an explicit pack failure without creating a package. Temporary artifacts are removed after validation.

Both packaging workflows are supported:

```bash
dotnet pack src/MapperForge/MapperForge.csproj -c Release
# Or, after a successful Release build:
dotnet pack src/MapperForge/MapperForge.csproj -c Release --no-build
```

## Performance

MapperForge emits plain C# assignments and static method calls. The benchmark project compares manual mapping with MapperForge generated mapping:

```bash
dotnet run -c Release --project benchmarks/MapperForge.Benchmarks
```

Benchmark baselines for AutoMapper and Mapster are intentionally left for a later milestone so the first version stays focused and transparent.

## Roadmap

The implementation specs and progress checklist are maintained in [Execution order](docs/specs/00-ordem-de-execucao.md). Start with MVP stabilization, then follow the dependencies and acceptance criteria recorded there.

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
