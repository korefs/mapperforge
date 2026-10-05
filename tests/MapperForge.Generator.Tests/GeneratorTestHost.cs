using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MapperForge.Generator;

namespace MapperForge.Generator.Tests;

internal static class GeneratorTestHost
{
    public static GeneratorRunResult Run(string source, params MetadataReference[] additionalReferences)
        => RunNamed("MapperForge.Generator.Tests.DynamicAssembly", source, additionalReferences);

    public static GeneratorRunResult RunNamed(string assemblyName, string source, params MetadataReference[] additionalReferences)
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
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new MapperForgeGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var runResult = driver.GetRunResult().Results.Single();
        if (runResult.Exception is not null)
            throw new InvalidOperationException("MapperForge generator failed during the test compilation.", runResult.Exception);
        var generatedSources = runResult.GeneratedSources
            .Select(static source => source.SourceText.ToString())
            .ToImmutableArray();

        return new GeneratorRunResult(outputCompilation, runResult.Diagnostics, generatedSources,
            runResult.GeneratedSources.Select(static source => source.HintName).ToImmutableArray());
    }

    public static T Execute<T>(GeneratorRunResult result, params GeneratorRunResult[] dependencies)
    {
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
        }

        var context = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        context.Resolving += (_, name) =>
        {
            var dependency = dependencies.SingleOrDefault(item => item.Compilation.AssemblyName == name.Name);
            if (dependency is null) return null;
            using var dependencyStream = new MemoryStream();
            var dependencyEmit = dependency.Compilation.Emit(dependencyStream);
            if (!dependencyEmit.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, dependencyEmit.Diagnostics));
            dependencyStream.Position = 0;
            return context.LoadFromStream(dependencyStream);
        };
        try
        {
            stream.Position = 0;
            var assembly = context.LoadFromStream(stream);
            return (T)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally { context.Unload(); }
    }

    public static MetadataReference CreateReference(string source)
        => CreateReference(RunNamed("MapperForge.Generator.Tests.BaseAssembly", source));

    public static MetadataReference CreateReference(GeneratorRunResult result)
    {
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);
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
