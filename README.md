# MapperForge

**Compile-time object mapping for .NET, with explicit C# you can inspect and debug.**

[![CI](https://github.com/korefs/mapperforge/actions/workflows/ci.yml/badge.svg)](https://github.com/korefs/mapperforge/actions/workflows/ci.yml)
![Target framework](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)
![Status](https://img.shields.io/badge/status-experimental-orange?style=flat-square)

[Get started](#get-started) · [Mapping](#mapping) · [Supported contract](#supported-contract) · [Diagnostics](#diagnostics) · [Development](#development)

MapperForge is a Roslyn incremental source generator for mapping entities, DTOs, commands, and view models. Declare a source on a partial destination type; the generator adds a static `From` method and typed extension methods. Generated mappings use property assignments and static calls without runtime reflection or mapper registration.

> [!NOTE]
> MapperForge is experimental. The public API and supported mapping shapes may change. The instructions below build a package from this repository; they do not require a published NuGet release.

## Features

- **Generated C#:** mappings are created during compilation and remain easy to step through.
- **Small API:** use `Destination.From(source)`, `source.MapTo<Destination>()`, or collection helpers.
- **Property customization:** rename, ignore, or transform destination properties with attributes.
- **Composed mappings:** map nested objects and supported collections using declared element maps.
- **Inheritance and multiple sources:** map inherited properties and generate distinct `From` overloads.
- **Build-time feedback:** diagnose incompatible members, invalid construction, transforms, and unsafe nullability conversions.

## Get started

The library targets `net8.0` and uses C# 12. The generator references Roslyn 4.11, so use an SDK with a compatible compiler; SDK **9.0.201** is verified for this workspace. Running the sample and tests also requires the .NET 8 runtime (including ASP.NET Core for the sample).

Clone the repository and build a local NuGet package:

```bash
git clone https://github.com/korefs/mapperforge.git
cd mapperforge
dotnet pack src/MapperForge/MapperForge.csproj -c Release -o artifacts/packages
```

From your application's project directory, install it using the absolute path to that package folder:

```bash
dotnet add package MapperForge --source /absolute/path/to/mapperforge/artifacts/packages
```

The package includes both the attributes/marker interface and the source generator in `analyzers/dotnet/cs`. No separate generator package is needed. Enable nullable annotations in your application for the nullability checks below.

### Your first mapping

Put this example in a console application's `Program.cs`:

```csharp
using MapperForge;

var user = new User(42, "Ada Lovelace", "ADA@EXAMPLE.COM");
var dto = UserDto.From(user);
var sameDto = user.MapTo<UserDto>();
var dtos = new[] { user }.MapToList<UserDto>();

Console.WriteLine($"{dto.Id}: {dto.Name} <{dto.Email}>");
// 42: Ada Lovelace <ada@example.com>

public sealed record User(int Id, string DisplayName, string Email);

[MapFrom(typeof(User))]
public sealed partial class UserDto
{
    public int Id { get; init; }

    [MapProperty(nameof(User.DisplayName))]
    public string Name { get; init; } = "";

    [MapTransform(nameof(NormalizeEmail))]
    public string Email { get; init; } = "";

    [MapIgnore]
    public string InternalNote { get; init; } = "created locally";

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
```

Matching properties such as `Id` map by name. `[MapProperty]` selects a different source property, `[MapTransform]` converts its value, and `[MapIgnore]` leaves a destination property at its constructed value. Attributes belong on destination properties. Destinations and their containing types must be `partial`.

## Mapping

### Declare maps

`IMapFrom<TSource>` is an alternative to `[MapFrom(typeof(TSource))]`:

```csharp
public partial class UserSummary : IMapFrom<User>
{
    public int Id { get; init; }
}
```

The marker has no members to implement. A destination can declare multiple source attributes or marker interfaces, producing one `From` overload per source. A source can map to several destinations. Repeated source/destination pairs, including attribute + marker declarations, generate only one map. Closed generic sources such as `Source<int>` and `Source<string>` remain distinct.

### Map objects and collections

| API | Result |
| --- | --- |
| `UserDto.From(user)` | A new `UserDto` through a generated static method. |
| `user.MapTo<UserDto>()` | A new `UserDto` through a generated extension for the declared source type. |
| `users.MapToList<UserDto>()` | A new `List<UserDto>` from `IEnumerable<User>`. |
| `users.MapTo<UserDto>()` | The same collection behavior as `MapToList`. |

Collection helpers enumerate once and validate the requested destination before enumeration, even for an empty input. Null reference roots throw `ArgumentNullException`. Unknown destinations throw `MapperForgeMappingException`; an input typed as `object` also takes this failure path, even if its runtime type has a map. Keep the source's declared type when calling the extensions.

### Map nested properties

Declare a map for each nested source/destination pair in the same compilation:

```csharp
public sealed class Address
{
    public string Street { get; init; } = "";
}

public sealed class Profile
{
    public Address? BillingAddress { get; init; }
    public List<Address> PreviousAddresses { get; init; } = [];
}

[MapFrom(typeof(Address))]
public partial class AddressDto
{
    public string Street { get; init; } = "";
}

[MapFrom(typeof(Profile))]
public partial class ProfileDto
{
    public AddressDto? BillingAddress { get; init; }
    public List<AddressDto> PreviousAddresses { get; init; } = [];
}
```

The generated mapping preserves a null `BillingAddress` and maps each previous address. Property collection conversion supports named enumerable source types into `List<T>` or `IReadOnlyList<T>` when an element map exists. Directly assignable properties are copied as-is, so object and collection references can be shared; mapping is not a general deep-copy operation.

### Transforms and nullability

Transforms must be accessible static, non-generic, non-void methods on the destination or its base classes. They take exactly one by-value parameter and return a value implicitly assignable to the destination property. Overloads prefer the best implicit input conversion, including exact matches; invalid or ambiguous candidates produce MFG004. Transform exceptions propagate to the caller.

| Source → destination | Behavior |
| --- | --- |
| Non-nullable → non-nullable | Assign, map, or transform. |
| Non-nullable → nullable | Allowed. |
| Nullable → nullable | Preserve null for direct, nested, and collection mappings. |
| Nullable → non-nullable | MFG008 unless a transform explicitly accepts nullable input and returns non-nullable output. |
| Unknown nullability → non-nullable | MFG013 warning when the destination contract cannot be verified. |

The policy applies to nullable value types, reference annotations, collection elements, and nested generic annotations. For nullable input and destination, a transform with non-nullable input is called only when the value is present. A transform declaring nullable input receives null and owns the fallback:

```csharp
// On a destination mapping a source with a nullable Email property:
[MapTransform(nameof(Normalize))]
public string Email { get; init; } = "";

private static string Normalize(string? email) => email?.Trim() ?? "";
```

Non-nullable annotations are a static contract; generated code does not check every property for null at runtime.

## Supported contract

| Area | Current support |
| --- | --- |
| Sources | Accessible named classes, structs, records, and interfaces, including closed generics. |
| Destinations | Accessible concrete, non-generic partial classes, structs, records, and record structs. |
| Nested destinations | Accessible non-generic partial containers; static class containers are allowed. |
| Construction | An unambiguous constructor callable without arguments, including private destination constructors and optional/`params` constructors. Required members must be mapped or covered by `[SetsRequiredMembers]`. |
| Properties | Public source getters and destination setters or `init` accessors accessible from the destination. Fields, indexers, and static properties are not mapped. Ignore computed properties with `[MapIgnore]`. |
| Inheritance | Source and destination base properties are included; the most derived declaration wins for `override` and `new`. Overrides use their own mapping attributes. |
| Interfaces | Inherited source properties are supported. Incompatible declarations require a prevailing declaration on a derived interface (MFG012). |
| Visibility | Public and internal models work. `From` becomes internal when either model is effectively internal; inaccessible nested/file-local types are rejected. |
| Existing methods | Other `From` overloads remain available; a conflicting `From(Source)` signature is rejected. `MapperForge.MapperForgeGeneratedExtensions` is a reserved type name. |

> [!IMPORTANT]
> Constructor-argument mapping, positional destinations requiring arguments, generic destinations/containers, abstract or static destinations, ref structs, open generic sources, and ref/pointer property signatures are unsupported. Automatic discovery of maps in referenced assemblies and recursive-map validation are not implemented. Use acyclic mapping graphs; cyclic input graphs can recurse indefinitely.

A source map declared for `IEnumerable<T>` is also rejected when a map for `T` would generate a conflicting collection extension. Array property materialization and collection conversions beyond the forms described above remain future work.

## Diagnostics

| ID | Severity | Meaning / action |
| --- | --- | --- |
| MFG001 | Error | Mark the destination and its containers `partial`. |
| MFG002 | Warning | No matching source property; rename or ignore the destination property. |
| MFG003 | Error | Incompatible property types; provide a supported nested map or transform. |
| MFG004 | Error | Missing, invalid, or ambiguous transform method. |
| MFG005 | Error | Destination property has no accessible setter. |
| MFG006 | Error | Unsupported type shape, accessibility, reserved name, or signature conflict. |
| MFG007 | Error | Destination cannot be constructed without arguments or has an unfulfilled required member. |
| MFG008 | Error | Nullable input cannot satisfy a non-nullable destination contract. |
| MFG012 | Error | Inherited interface property declarations are incompatible. |
| MFG013 | Warning | Source nullability cannot be verified; enable annotations or declare a transform contract. |

MFG009–MFG011 are reserved for future dependency, recursion, and external mapping contract diagnostics; they are not currently emitted.

## Development

Run these commands from the repository root:

```bash
dotnet restore MapperForge.sln
dotnet build MapperForge.sln -c Release --no-restore
dotnet test MapperForge.sln -c Release --no-build
```

| Location | Purpose |
| --- | --- |
| [`src/MapperForge`](src/MapperForge) | Public attributes, marker interface, and runtime failure API. |
| [`src/MapperForge.Generator`](src/MapperForge.Generator) | Mapping discovery, validation, diagnostics, and C# emission. |
| [`tests`](tests) | Generator compilation/execution tests, runtime tests, and a package-only consumer. |
| [`samples/MapperForge.Sample.Api`](samples/MapperForge.Sample.Api) | ASP.NET Core API showing DTO mapping. |
| [`benchmarks/MapperForge.Benchmarks`](benchmarks/MapperForge.Benchmarks) | BenchmarkDotNet comparison against manual mapping. |
| [`scripts/verify-package.py`](scripts/verify-package.py) | Isolated package and consumer validation. |

### Run the API sample

```bash
dotnet run --project samples/MapperForge.Sample.Api --launch-profile http
```

Open [Swagger UI](http://localhost:5026/swagger) or try:

```bash
curl http://localhost:5026/users
curl http://localhost:5026/users/1
```

The sample exposes `GET /users`, `GET /users/{id}`, and `POST /users`. It keeps users in memory and demonstrates property renaming, email normalization, and collection mapping.

### Validate packaging

```bash
python3 scripts/verify-package.py
```

Requires Python 3, a compatible .NET SDK/runtime, and NuGet access. The script uses a clean temporary source copy and isolated package caches to check clean packing, build → pack `--no-build`, exactly one generator DLL, and a consumer referencing only the local package. It also checks that a missing generator DLL fails packing. Temporary artifacts are cleaned up automatically.

After a successful Release build, you can also pack with:

```bash
dotnet pack src/MapperForge/MapperForge.csproj -c Release --no-build -o artifacts/packages
```

The [CI workflow](.github/workflows/ci.yml) restores, builds, tests, and uploads packages. The isolated Python package check is currently a separate local command.

### Run benchmarks

```bash
dotnet run -c Release --project benchmarks/MapperForge.Benchmarks
```

The benchmark compares generated `From` mapping with equivalent manual assignments and reports allocations. AutoMapper and Mapster comparisons are not included; no performance claims are made beyond what you measure locally.

## Planned work

- Discover and reuse maps from referenced assemblies.
- Map constructor arguments and positional records.
- Expand collection conversion and materialization.
- Validate mapping dependencies and reject recursive graphs.
- Add SDK compatibility tracking, analyzer release tracking, and code fixes.
- Explore global conventions, configuration, and dependency injection helpers.

The earlier `MapperForgeOptions.Default` placeholder has been removed; it had no mapping behavior. Global configuration needs a future API contract.
