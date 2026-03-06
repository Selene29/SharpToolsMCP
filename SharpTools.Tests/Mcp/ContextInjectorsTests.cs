using SharpTools.Tools.Mcp;

namespace SharpTools.Tests.Mcp;

public class ContextInjectorsTests
{
    [Test]
    public async Task CreateCodeDiff_WithIdenticalCode_ReturnsNoChanges()
    {
        var code = "public class Test { }";

        var result = ContextInjectors.CreateCodeDiff(code, code);

        await Assert.That(result).Contains("No changes detected");
    }

    [Test]
    public async Task CreateCodeDiff_WithInsertedLine_ShowsAddition()
    {
        var oldCode = "public class Test { }";
        var newCode = "public class Test { public void Method() { } }";

        var result = ContextInjectors.CreateCodeDiff(oldCode, newCode);

        await Assert.That(result).Contains("+");
        await Assert.That(result).Contains("diff");
    }

    [Test]
    public async Task CreateCodeDiff_WithDeletedLine_ShowsDeletion()
    {
        var oldCode = @"public class Test
{
    public void OldMethod() { }
}";
        var newCode = @"public class Test
{
}";

        var result = ContextInjectors.CreateCodeDiff(oldCode, newCode);

        await Assert.That(result).Contains("-");
        await Assert.That(result).Contains("diff");
    }

    [Test]
    public async Task CreateCodeDiff_WithChanges_ContainsAppliedNote()
    {
        var oldCode = "public class Test { }";
        var newCode = "public class Changed { }";

        var result = ContextInjectors.CreateCodeDiff(oldCode, newCode);

        await Assert.That(result).Contains("This diff has been applied");
    }

    [Test]
    public async Task CreateCodeDiff_WithMultipleChanges_ShowsBothAdditionsAndDeletions()
    {
        var oldCode = @"public class Test
{
    public int Field1;
    public string Field2;
}";
        var newCode = @"public class Test
{
    public int ModifiedField;
    public bool NewField;
}";

        var result = ContextInjectors.CreateCodeDiff(oldCode, newCode);

        await Assert.That(result).Contains("+");
        await Assert.That(result).Contains("-");
    }

    [Test]
    public async Task CreateCodeDiff_NormalizesWhitespace()
    {
        // Identical content with different whitespace should show no changes
        var oldCode = "public class Test { }";
        var newCode = "  public class Test { }  ";

        var result = ContextInjectors.CreateCodeDiff(oldCode, newCode);

        await Assert.That(result).Contains("No changes detected");
    }
}
