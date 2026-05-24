using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MapperForge.Generator;

namespace MapperForge.Generator.Tests;

internal static class GeneratorTestHost
{
    public static GeneratorRunResult Run(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(MapFromAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "MapperForge.Generator.Tests.DynamicAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new MapperForgeGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var runResult = driver.GetRunResult().Results.Single();
        var generatedSources = runResult.GeneratedSources
            .Select(static source => source.SourceText.ToString())
            .ToImmutableArray();

        return new GeneratorRunResult(outputCompilation, runResult.Diagnostics, generatedSources);
    }
}

internal sealed class GeneratorRunResult
{
    public GeneratorRunResult(Compilation compilation, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<string> generatedSources)
    {
        Compilation = compilation;
        Diagnostics = diagnostics;
        GeneratedSources = generatedSources;
    }

    public Compilation Compilation { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public ImmutableArray<string> GeneratedSources { get; }
}
