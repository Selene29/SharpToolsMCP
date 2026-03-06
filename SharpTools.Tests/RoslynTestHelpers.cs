using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpTools.Tests;

/// <summary>
/// Shared utilities for creating in-memory Roslyn compilations in tests.
/// </summary>
internal static class RoslynTestHelpers
{
    private static readonly MetadataReference[] CommonReferences =
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location)
    };

    /// <summary>
    /// Creates a compilation and retrieves the first method symbol from the code.
    /// </summary>
    public static (Compilation Compilation, IMethodSymbol MethodSymbol) CreateCompilationWithMethod(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            CommonReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var methodDeclaration = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .First();
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration)!;

        return (compilation, methodSymbol);
    }

    /// <summary>
    /// Creates a compilation and retrieves a named type symbol from the code.
    /// </summary>
    public static (Compilation Compilation, INamedTypeSymbol TypeSymbol) CreateCompilationWithType(string code, string fullyQualifiedTypeName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            CommonReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var typeSymbol = compilation.GetTypeByMetadataName(fullyQualifiedTypeName)!;
        return (compilation, typeSymbol);
    }
}
