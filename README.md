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

The marker interface is an equivalent declaration:

```csharp
public partial class UserDto : IMapFrom<User>
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}
```

Interfaces can compose `IMapFrom<TSource>` contracts for concrete destinations. The generator combines marker interfaces and attributes, including repeated pairs across partial declarations, and generates each pair once. The marker has no runtime members to implement.

## Supported Contract

| Area | Current support |
| --- | --- |
| Source | Accessible named classes, structs, records and interfaces, including closed generics. |
| Destination | Accessible, concrete, non-generic partial classes, structs and simple records. |
| Containers | Accessible, non-generic partial classes, structs or records. Static class containers are allowed. |
| Accessibility | Public and internal models work. `From` is internal when either model is effectively internal; helper methods with internal source signatures are internal. Private/protected nested and file-local types inaccessible to helpers produce MFG006. |
| Construction | An accessible constructor callable without arguments, including private constructors on the partial destination and unambiguous optional/params constructors. Required members must be mapped or covered by `[SetsRequiredMembers]`; otherwise MFG007. |
| Members | Public source getters and accessible destination setters/init accessors. Use `[MapIgnore]` for computed members. Ref/pointer property signatures are unsupported. |
| Existing methods | An existing conflicting `From(Source)` signature produces MFG006. Other overloads remain available. |
| Object-typed input | `object.MapTo<TDestination>()` throws `MapperForgeMappingException`, including when the runtime type has a map. Explicit `object` source declarations are rejected with MFG006. |
| Collections | Top-level `MapToList<TDestination>` and enumerable `MapTo<TDestination>` return `List<TDestination>`. Property conversion supports named enumerable sources into `List<T>`/`IReadOnlyList<T>` when an element map exists; additional materialization is planned in stage 5. |

Abstract/static destinations, ref structs, open generic sources, generic destinations and generic destination containers produce MFG006. A local `MapperForge.MapperForgeGeneratedExtensions` declaration is reserved. A source declared as `IEnumerable<T>` alongside a map for `T` is rejected when its root extension would collide with the collection overload.

Collections validate the requested destination before calling `GetEnumerator`, including for empty inputs. Supported collections enumerate once. Directly assignable collections share their existing reference; they are not deep copies. Nullable reference annotations can be widened on CLR-identical generic types through an explicit annotation view without materializing a collection.

Mapping constructor arguments, positional destination records with required constructor parameters, external map discovery and recursive-map validation remain scheduled in later stages.

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

Repeating the same source/destination pair is idempotent, including across partial declarations. Closed generic sources such as `Source<int>` and `Source<string>` remain distinct in direct, nested, and collection mappings. Generated filenames include a stable SHA-256 suffix to distinguish names that sanitize identically. Generic destinations and generic destination containers produce MFG006.

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

Transform methods must be accessible static ordinary methods, non-generic, non-void, with exactly one by-value parameter and a return type implicitly assignable to the destination member. Ref/out/in parameters are rejected. Overloads use the best implicit input conversion, preferring an exact match; missing, incompatible or ambiguous candidates produce MFG004. Transform exceptions propagate to the caller.

## Null Policy

| Input → destination | Behavior |
| --- | --- |
| Non-nullable → non-nullable | Copy, map or transform the value. |
| Non-nullable → nullable | Allow; do not add a null value. |
| Nullable → nullable | Preserve null for direct assignments and nested/collection mapping. |
| Nullable → non-nullable | MFG008 unless an explicit transform declares nullable input and non-nullable output. |
| Unannotated → non-nullable | MFG013 warning when a non-nullable obligation cannot be verified. |

The policy covers nullable value types and reference annotations, including collection elements and nested generic annotations. For a nullable input and nullable destination, a transform with non-nullable input runs only for a non-null value. A transform declaring nullable input receives null and is responsible for its result contract. Nullable return values cannot feed non-nullable members.

Every reference root API rejects null with `ArgumentNullException`. Non-nullable annotations are a static contract; generated mappings do not add a runtime null check to every property. Missing annotations remain visible through MFG013 rather than null-forgiving operators.

As an example, an explicit fallback can satisfy a non-nullable destination:

```csharp
[MapTransform(nameof(Normalize))]
public string Email { get; init; } = "";

private static string Normalize(string? email) => email?.Trim() ?? "";
```

**Experimental API change:** `MapperForgeOptions` was a placeholder with no mapping behavior and has been removed. Remove references to `MapperForgeOptions.Default`; global configuration will require a future contract.

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
| MFG001 | Error | Destination or container declaring an attribute/marker map is not partial. |
| MFG002 | Warning | Destination member could not be mapped from the source type. |
| MFG003 | Error | Source and destination member types are incompatible. |
| MFG004 | Error | Transform method was not found or has an invalid or ambiguous signature. |
| MFG005 | Error | Destination member has no accessible setter. |
| MFG006 | Error | Unsupported type shape, accessibility, name or generated signature conflict. |
| MFG007 | Error | No valid construction without arguments, or an unfulfilled required member. |
| MFG008 | Error | Nullable value cannot satisfy a non-nullable obligation without explicit handling. |
| MFG009 | Error (reserved) | Invalid/unavailable mapping dependency; detailed planning is scheduled in stage 6. |
| MFG010 | Error (reserved) | Recursive mapping dependency; scheduled in stage 6. |
| MFG011 | Error (reserved) | Incompatible, malformed or ambiguous external mapping contract; scheduled in stage 3. |
| MFG012 | Error | Inherited interface members have incompatible declarations without a prevailing declaration. |
| MFG013 | Warning | Source nullability is unknown for a non-nullable obligation. |

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
