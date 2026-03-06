using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using NSubstitute;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Mcp;

namespace SharpTools.Tests.Mcp;

public class ToolHelpersTests
{
    [Test]
    public async Task ToJson_WithSimpleObject_ReturnsValidJson()
    {
        var data = new { Name = "Test", Value = 42 };

        var json = ToolHelpers.ToJson(data);

        await Assert.That(json).Contains("\"name\"");
        await Assert.That(json).Contains("\"value\"");
        await Assert.That(json).Contains("42");
    }

    [Test]
    public async Task ToJson_WithNull_ReturnsNullJson()
    {
        var json = ToolHelpers.ToJson(null);

        await Assert.That(json).IsEqualTo("null");
    }

    [Test]
    public async Task ToJson_WithNullProperties_OmitsNullValues()
    {
        var data = new { Name = "Test", NullProp = (string?)null };

        var json = ToolHelpers.ToJson(data);

        await Assert.That(json).DoesNotContain("nullProp");
    }

    [Test]
    public async Task RemoveGlobalPrefix_WithGlobalPrefix_RemovesIt()
    {
        var result = ToolHelpers.RemoveGlobalPrefix("global::System.String");

        await Assert.That(result).IsEqualTo("System.String");
    }

    [Test]
    public async Task RemoveGlobalPrefix_WithoutGlobalPrefix_ReturnsOriginal()
    {
        var result = ToolHelpers.RemoveGlobalPrefix("System.String");

        await Assert.That(result).IsEqualTo("System.String");
    }

    [Test]
    public async Task RemoveGlobalPrefix_WithEmptyString_ReturnsEmpty()
    {
        var result = ToolHelpers.RemoveGlobalPrefix("");

        await Assert.That(result).IsEqualTo("");
    }

    [Test]
    public async Task RemoveGlobalPrefix_WithNull_ReturnsNull()
    {
        var result = ToolHelpers.RemoveGlobalPrefix(null!);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TrimBackslash_WithLeadingBackslash_RemovesIt()
    {
        var result = "\\test".TrimBackslash();

        await Assert.That(result).IsEqualTo("test");
    }

    [Test]
    public async Task TrimBackslash_WithoutLeadingBackslash_ReturnsOriginal()
    {
        var result = "test".TrimBackslash();

        await Assert.That(result).IsEqualTo("test");
    }

    [Test]
    public async Task NormalizeEndOfLines_WithMixedLineEndings_NormalizesToLF()
    {
        var result = "line1\r\nline2\rline3\nline4".NormalizeEndOfLines();

        await Assert.That(result).IsEqualTo("line1\nline2\nline3\nline4");
    }

    [Test]
    public async Task NormalizeEndOfLines_WithOnlyLF_ReturnsSame()
    {
        var result = "line1\nline2".NormalizeEndOfLines();

        await Assert.That(result).IsEqualTo("line1\nline2");
    }

    [Test]
    public async Task EnsureSolutionLoaded_WhenSolutionNotLoaded_ThrowsMcpException()
    {
        var solutionManager = Substitute.For<ISolutionManager>();
        solutionManager.IsSolutionLoaded.Returns(false);

        await Assert.That(() => ToolHelpers.EnsureSolutionLoaded(solutionManager))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task EnsureSolutionLoaded_WhenSolutionIsLoaded_DoesNotThrow()
    {
        var solutionManager = Substitute.For<ISolutionManager>();
        solutionManager.IsSolutionLoaded.Returns(true);

        // Should not throw
        ToolHelpers.EnsureSolutionLoaded(solutionManager);

        await Assert.That(solutionManager.IsSolutionLoaded).IsTrue();
    }

    [Test]
    public async Task EnsureSolutionLoadedWithDetails_WhenSolutionNotLoaded_ThrowsMcpException()
    {
        var solutionManager = Substitute.For<ISolutionManager>();
        var logger = Substitute.For<ILogger<ToolHelpersTests>>();
        solutionManager.IsSolutionLoaded.Returns(false);

        await Assert.That(() => ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, "TestOp"))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task GetSymbolKindString_WithMethodSymbol_ReturnsMethod()
    {
        var code = @"
public class TestClass
{
    public void TestMethod() { }
}";
        var (_, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);

        var result = ToolHelpers.GetSymbolKindString(methodSymbol);

        await Assert.That(result).IsEqualTo("Method");
    }

    [Test]
    public async Task GetSymbolKindString_WithTypeSymbol_ReturnsClass()
    {
        var code = @"
namespace TestNs
{
    public class TestClass
    {
        public void M() { }
    }
}";
        var (_, typeSymbol) = RoslynTestHelpers.CreateCompilationWithType(code, "TestNs.TestClass");

        var result = ToolHelpers.GetSymbolKindString(typeSymbol);

        await Assert.That(result).IsEqualTo("Class");
    }

    [Test]
    public async Task GetRoslynSymbolModifiersString_PublicStaticMethod_ReturnsPublicStatic()
    {
        var code = @"
public class TestClass
{
    public static void StaticMethod() { }
}";
        var (_, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);

        var result = ToolHelpers.GetRoslynSymbolModifiersString(methodSymbol);

        await Assert.That(result).Contains("public");
        await Assert.That(result).Contains("static");
    }

    [Test]
    public async Task GetRoslynSymbolModifiersString_AbstractMethod_ReturnsAbstract()
    {
        var code = @"
public abstract class TestClass
{
    public abstract void AbstractMethod();
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            new[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodDecl = syntaxTree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .First();
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl)!;

        var result = ToolHelpers.GetRoslynSymbolModifiersString(methodSymbol);

        await Assert.That(result).Contains("abstract");
    }

    [Test]
    public async Task IsPropertyAccessor_WithMethod_ReturnsFalse()
    {
        var code = @"
public class TestClass
{
    public void RegularMethod() { }
}";
        var (_, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);

        var result = ToolHelpers.IsPropertyAccessor(methodSymbol);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetRoslynTypeSpecificModifiersString_StaticClass_ReturnsPublicStatic()
    {
        var code = @"
namespace TestNs
{
    public static class StaticClass
    {
        public static void M() { }
    }
}";
        var (_, typeSymbol) = RoslynTestHelpers.CreateCompilationWithType(code, "TestNs.StaticClass");

        var result = ToolHelpers.GetRoslynTypeSpecificModifiersString(typeSymbol);

        await Assert.That(result).Contains("public");
        await Assert.That(result).Contains("static");
    }

    [Test]
    public async Task GetRoslynTypeSpecificModifiersString_AbstractClass_ReturnsAbstract()
    {
        var code = @"
namespace TestNs
{
    public abstract class AbstractClass
    {
        public abstract void M();
    }
}";
        var (_, typeSymbol) = RoslynTestHelpers.CreateCompilationWithType(code, "TestNs.AbstractClass");

        var result = ToolHelpers.GetRoslynTypeSpecificModifiersString(typeSymbol);

        await Assert.That(result).Contains("abstract");
    }

    [Test]
    public async Task GetRoslynTypeSpecificModifiersString_SealedClass_ReturnsSealedPublic()
    {
        var code = @"
namespace TestNs
{
    public sealed class SealedClass
    {
        public void M() { }
    }
}";
        var (_, typeSymbol) = RoslynTestHelpers.CreateCompilationWithType(code, "TestNs.SealedClass");

        var result = ToolHelpers.GetRoslynTypeSpecificModifiersString(typeSymbol);

        await Assert.That(result).Contains("sealed");
    }
}
