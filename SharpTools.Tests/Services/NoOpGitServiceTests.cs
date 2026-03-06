using SharpTools.Tools.Services;

namespace SharpTools.Tests.Services;

public class NoOpGitServiceTests
{
    private NoOpGitService _service = null!;

    [Before(Test)]
    public void Setup()
    {
        _service = new NoOpGitService();
    }

    [Test]
    public async Task IsRepositoryAsync_ReturnsFalse()
    {
        var result = await _service.IsRepositoryAsync("/some/path");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsOnSharpToolsBranchAsync_ReturnsFalse()
    {
        var result = await _service.IsOnSharpToolsBranchAsync("/some/path");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task EnsureSharpToolsBranchAsync_CompletesSuccessfully()
    {
        // Should complete without throwing
        var task = _service.EnsureSharpToolsBranchAsync("/some/path");
        await task;

        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task CommitChangesAsync_CompletesSuccessfully()
    {
        var task = _service.CommitChangesAsync("/some/path", new[] { "file1.cs" }, "test commit");
        await task;

        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task RevertLastCommitAsync_ReturnsFalseAndEmptyDiff()
    {
        var (success, diff) = await _service.RevertLastCommitAsync("/some/path");

        await Assert.That(success).IsFalse();
        await Assert.That(diff).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetBranchOriginCommitAsync_ReturnsEmptyString()
    {
        var result = await _service.GetBranchOriginCommitAsync("/some/path");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task CreateUndoBranchAsync_ReturnsEmptyString()
    {
        var result = await _service.CreateUndoBranchAsync("/some/path");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetDiffAsync_ReturnsEmptyString()
    {
        var result = await _service.GetDiffAsync("/some/path", "sha1", "sha2");

        await Assert.That(result).IsEqualTo(string.Empty);
    }
}
