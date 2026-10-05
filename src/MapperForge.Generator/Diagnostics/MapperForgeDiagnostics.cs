using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Diagnostics;

internal static class MapperForgeDiagnostics
{
    private const string Category = "MapperForge";

    public static readonly DiagnosticDescriptor UnsupportedMapping = new(
        "MFG006", "Mapping shape is not supported", "Cannot generate mapping from '{0}' to '{1}': {2}.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidConstruction = new(
        "MFG007", "Destination cannot be constructed", "Cannot construct destination '{0}': {1}.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NullableToNonNullable = new(
        "MFG008", "Nullable value requires explicit handling",
        "Cannot map nullable type '{0}' to non-nullable type '{1}' for member '{2}'. Use a transform with nullable input and non-nullable output.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    // Reserved for dependency planning and recursive maps in subsequent stages.
    public static readonly DiagnosticDescriptor InvalidDependency = new(
        "MFG009", "Mapping dependency is unavailable", "Mapping dependency from '{0}' to '{1}' is invalid or unavailable for '{2}'.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    public static readonly DiagnosticDescriptor RecursiveMapping = new(
        "MFG010", "Recursive mapping is not supported", "Mapping from '{0}' to '{1}' has a recursive dependency: {2}.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    public static readonly DiagnosticDescriptor InvalidExternalContract = new(
        "MFG011", "External mapping contract is incompatible", "External mapping contract in '{0}' is incompatible or ambiguous: {1}.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownNullability = new(
        "MFG013", "Source nullability is unknown",
        "Nullability of type '{0}' cannot be verified for non-nullable type '{1}' in member '{2}'. Enable nullable annotations or declare an explicit transform contract.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousInheritedMember = new(
        "MFG012",
        "Inherited member is ambiguous",
        "Type '{0}' inherits incompatible declarations of member '{1}'. Declare the member on a derived interface to resolve the ambiguity.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DestinationMustBePartial = new(
        "MFG001",
        "Destination type must be partial",
        "Type '{0}' must be partial to use MapperForge source generation.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DestinationMemberNotMapped = new(
        "MFG002",
        "Destination member could not be mapped",
        "Destination member '{0}' could not be mapped from source type '{1}'. Use [MapProperty], [MapIgnore], or provide a transform.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncompatibleTypes = new(
        "MFG003",
        "Member types are incompatible",
        "Cannot map source member '{0}' of type '{1}' to destination member '{2}' of type '{3}'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TransformMethodInvalid = new(
        "MFG004",
        "Transform method is invalid",
        "Transform method '{0}' was not found or has an invalid or ambiguous signature for '{1}' -> '{2}'. Expected an accessible, non-generic static ordinary method with one by-value parameter and a compatible return type.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DestinationSetterNotAccessible = new(
        "MFG005",
        "Destination member has no accessible setter",
        "Destination member '{0}' does not have an accessible setter.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
