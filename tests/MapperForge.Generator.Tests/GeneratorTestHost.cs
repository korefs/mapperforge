using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MapperForge.Generator;

namespace MapperForge.Generator.Tests;

internal static class GeneratorTestHost
{
    public static GeneratorRunResult Run(string source, params MetadataReference[] additionalReferences)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(MapFromAttribute).Assembly.Location));
        references.AddRange(additionalReferences);

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

        return new GeneratorRunResult(outputCompilation, runResult.Diagnostics, generatedSources,
            runResult.GeneratedSources.Select(static source => source.HintName).ToImmutableArray());
    }

    public static T Execute<T>(GeneratorRunResult result)
    {
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
        }

        var assembly = Assembly.Load(stream.ToArray());
        return (T)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
    }

    public static MetadataReference CreateReference(string source)
    {
        using var stream = new MemoryStream();
        var emitted = Run(source).Compilation.WithAssemblyName("MapperForge.Generator.Tests.BaseAssembly").Emit(stream);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}

internal sealed class GeneratorRunResult
{
    public GeneratorRunResult(Compilation compilation, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<string> generatedSources,
        ImmutableArray<string> generatedHintNames)
    {
        Compilation = compilation;
        Diagnostics = diagnostics;
        GeneratedSources = generatedSources;
        GeneratedHintNames = generatedHintNames;
    }

    public Compilation Compilation { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public ImmutableArray<string> GeneratedSources { get; }

    public ImmutableArray<string> GeneratedHintNames { get; }
}
