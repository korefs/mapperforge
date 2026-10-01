using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace MapperForge.Generator.Tests;

internal static class ContractTestAssertions
{
    public static void Compiles(GeneratorRunResult result)
    {
        result.Diagnostics.Should().BeEmpty();
        CompilerIsClean(result);
    }

    public static void Rejects(GeneratorRunResult result, string diagnosticId)
    {
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == diagnosticId && diagnostic.Severity == DiagnosticSeverity.Error);
        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8785");
        CompilerIsClean(result);
    }

    public static void CompilerIsClean(GeneratorRunResult result)
    {
        result.Compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Should().BeEmpty();
    }
}
