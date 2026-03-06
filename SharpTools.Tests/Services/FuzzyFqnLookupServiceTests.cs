using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Services;

namespace SharpTools.Tests.Services;

public class FuzzyFqnLookupServiceTests
{
    private ILogger<FuzzyFqnLookupService> _logger = null!;
    private FuzzyFqnLookupService _service = null!;

    [Before(Test)]
    public void Setup()
    {
        _logger = Substitute.For<ILogger<FuzzyFqnLookupService>>();
        _service = new FuzzyFqnLookupService(_logger);
    }

    [Test]
    public async Task IsPartialType_WithNonPartialClass_ReturnsFalse()
    {
        var code = @"
namespace TestNs
{
    public class NonPartialClass
    {
        public void Method() { }
    }
}";
        var (_, typeSymbol) = CreateCompilationWithType(code, "NonPartialClass");

        var result = FuzzyFqnLookupService.IsPartialType(typeSymbol);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPartialType_WithPartialClass_ReturnsTrue()
    {
        var code = @"
namespace TestNs
{
    public partial class PartialClass
    {
        public void Method() { }
    }
}";
        var (_, typeSymbol) = CreateCompilationWithType(code, "PartialClass");

        var result = FuzzyFqnLookupService.IsPartialType(typeSymbol);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPartialType_WithMethodSymbol_ReturnsFalse()
    {
        var code = @"
namespace TestNs
{
    public class TestClass
    {
        public void TestMethod() { }
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodDeclaration = syntaxTree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .First();
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration)!;

        var result = FuzzyFqnLookupService.IsPartialType(methodSymbol);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task FindMatchesAsync_WhenSolutionNotLoaded_ReturnsEmpty()
    {
        var solutionManager = Substitute.For<ISolutionManager>();
        solutionManager.IsSolutionLoaded.Returns(false);

        var result = await _service.FindMatchesAsync("SomeType", solutionManager, CancellationToken.None);

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        await Assert.That(() => new FuzzyFqnLookupService(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    private static (Compilation, INamedTypeSymbol) CreateCompilationWithType(string code, string typeName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location)
        };

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var typeSymbol = compilation.GetTypeByMetadataName($"TestNs.{typeName}")!;
        return (compilation, typeSymbol);
    }
}
