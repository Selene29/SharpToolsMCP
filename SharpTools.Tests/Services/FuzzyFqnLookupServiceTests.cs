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
        var (_, typeSymbol) = RoslynTestHelpers.CreateCompilationWithType(code, "TestNs.NonPartialClass");

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
        var (_, typeSymbol) = RoslynTestHelpers.CreateCompilationWithType(code, "TestNs.PartialClass");

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
        var (_, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);

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
}
