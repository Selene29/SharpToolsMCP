using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Services;

namespace SharpTools.Tests.Services;

public class ComplexityAnalysisServiceTests
{
    private ISolutionManager _solutionManager = null!;
    private ILogger<ComplexityAnalysisService> _logger = null!;
    private ComplexityAnalysisService _service = null!;

    [Before(Test)]
    public void Setup()
    {
        _solutionManager = Substitute.For<ISolutionManager>();
        _logger = Substitute.For<ILogger<ComplexityAnalysisService>>();
        _service = new ComplexityAnalysisService(_solutionManager, _logger);
    }

    [Test]
    public async Task AnalyzeMethodAsync_SimpleMethod_HasBasicComplexityOfOne()
    {
        var code = @"
public class TestClass
{
    public void SimpleMethod()
    {
        var x = 1;
        var y = 2;
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(1);
        await Assert.That((int)metrics["cognitiveComplexity"]).IsEqualTo(0);
    }

    [Test]
    public async Task AnalyzeMethodAsync_MethodWithIf_IncrementsCyclomaticComplexity()
    {
        var code = @"
public class TestClass
{
    public void MethodWithIf(int x)
    {
        if (x > 0)
        {
            var y = x + 1;
        }
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(2); // 1 base + 1 if
    }

    [Test]
    public async Task AnalyzeMethodAsync_MethodWithForLoop_IncrementsCyclomaticComplexity()
    {
        var code = @"
public class TestClass
{
    public void MethodWithForLoop()
    {
        for (int i = 0; i < 10; i++)
        {
            var x = i;
        }
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(2); // 1 base + 1 for
    }

    [Test]
    public async Task AnalyzeMethodAsync_MethodWithWhileLoop_IncrementsCyclomaticComplexity()
    {
        var code = @"
public class TestClass
{
    public void MethodWithWhile(int x)
    {
        while (x > 0)
        {
            x--;
        }
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(2); // 1 base + 1 while
    }

    [Test]
    public async Task AnalyzeMethodAsync_MethodWithLogicalOperators_IncrementsCyclomaticComplexity()
    {
        var code = @"
public class TestClass
{
    public void MethodWithLogical(bool a, bool b, bool c)
    {
        if (a && b || c)
        {
            var x = 1;
        }
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        // 1 base + 1 if + 1 && + 1 || = 4
        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(4);
    }

    [Test]
    public async Task AnalyzeMethodAsync_ComplexMethod_CalculatesMultipleComplexityContributors()
    {
        var code = @"
public class TestClass
{
    public void ComplexMethod(int x)
    {
        if (x > 0)
        {
            for (int i = 0; i < x; i++)
            {
                try
                {
                    var y = i;
                }
                catch (System.Exception)
                {
                    var z = 0;
                }
            }
        }
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        // 1 base + 1 if + 1 for + 1 catch = 4
        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(4);
    }

    [Test]
    public async Task AnalyzeMethodAsync_RecordsBasicMetrics()
    {
        var code = @"
public class TestClass
{
    public void MethodWithParams(int a, string b, bool c, double d, float e)
    {
        var x = 1;
        var y = 2;
        var z = 3;
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        await Assert.That((int)metrics["parameterCount"]).IsEqualTo(5);
        await Assert.That((int)metrics["localVariableCount"]).IsEqualTo(3);
        await Assert.That(metrics.ContainsKey("lineCount")).IsTrue();
        await Assert.That(metrics.ContainsKey("statementCount")).IsTrue();
    }

    [Test]
    public async Task AnalyzeMethodAsync_HighParameterCount_GeneratesRecommendation()
    {
        var code = @"
public class TestClass
{
    public void TooManyParams(int a, string b, bool c, double d, float e)
    {
        var x = 1;
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        await Assert.That(recommendations.Any(r => r.Contains("parameters"))).IsTrue();
    }

    [Test]
    public async Task AnalyzeMethodAsync_MethodWithNestedIfAndLoop_CalculatesCognitiveComplexity()
    {
        var code = @"
public class TestClass
{
    public void NestedMethod(int x)
    {
        if (x > 0)
        {
            for (int i = 0; i < x; i++)
            {
                if (i > 5)
                {
                    var y = i;
                }
            }
        }
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        // Cognitive complexity should account for nesting
        await Assert.That((int)metrics["cognitiveComplexity"]).IsGreaterThan(0);
        // Cyclomatic: 1 base + 1 if + 1 for + 1 if = 4
        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(4);
    }

    [Test]
    public async Task AnalyzeMethodAsync_MethodWithTernary_IncrementsCyclomaticComplexity()
    {
        var code = @"
public class TestClass
{
    public int MethodWithTernary(int x)
    {
        return x > 0 ? x : -x;
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        // 1 base + 1 ternary = 2
        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(2);
    }

    [Test]
    public async Task AnalyzeMethodAsync_MethodWithDoWhile_IncrementsCyclomaticComplexity()
    {
        var code = @"
public class TestClass
{
    public void MethodWithDoWhile(int x)
    {
        do
        {
            x--;
        } while (x > 0);
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        // 1 base + 1 do-while = 2
        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(2);
    }

    [Test]
    public async Task AnalyzeMethodAsync_MethodWithForeach_IncrementsCyclomaticComplexity()
    {
        var code = @"
using System.Collections.Generic;
public class TestClass
{
    public void MethodWithForeach(List<int> items)
    {
        foreach (var item in items)
        {
            var x = item;
        }
    }
}";
        var (compilation, methodSymbol) = RoslynTestHelpers.CreateCompilationWithMethod(code);
        _solutionManager.CurrentSolution.Returns((Solution?)null);

        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await _service.AnalyzeMethodAsync(methodSymbol, metrics, recommendations, CancellationToken.None);

        // 1 base + 1 foreach = 2
        await Assert.That((int)metrics["cyclomaticComplexity"]).IsEqualTo(2);
    }
}
