using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Diagnostics;

internal static class MapperForgeDiagnostics
{
    private const string Category = "MapperForge";

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
        "Transform method '{0}' was not found or has an invalid signature.",
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
