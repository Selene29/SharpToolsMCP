using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Services;

namespace SharpTools.Tests.Services;

public class DocumentOperationsServiceTests
{
    private ISolutionManager _solutionManager = null!;
    private ICodeModificationService _modificationService = null!;
    private IGitService _gitService = null!;
    private ILogger<DocumentOperationsService> _logger = null!;
    private DocumentOperationsService _service = null!;

    [Before(Test)]
    public void Setup()
    {
        _solutionManager = Substitute.For<ISolutionManager>();
        _modificationService = Substitute.For<ICodeModificationService>();
        _gitService = Substitute.For<IGitService>();
        _logger = Substitute.For<ILogger<DocumentOperationsService>>();
        _service = new DocumentOperationsService(_solutionManager, _modificationService, _gitService, _logger);
    }

    [Test]
    public async Task ReadFileAsync_WithExistingFileOutsideSolution_ThrowsAccessException()
    {
        var tempFile = Path.GetTempFileName();
        var content = "line1\nline2\nline3";
        await File.WriteAllTextAsync(tempFile, content);

        // Solution not loaded - path won't be readable
        _solutionManager.IsSolutionLoaded.Returns(false);

        try
        {
            await Assert.That(async () => await _service.ReadFileAsync(tempFile, false, CancellationToken.None))
                .ThrowsExactly<UnauthorizedAccessException>();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ReadFileAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        await Assert.That(async () => await _service.ReadFileAsync("/nonexistent/file.cs", false, CancellationToken.None))
            .ThrowsExactly<FileNotFoundException>();
    }

    [Test]
    public async Task FileExists_WithExistingFile_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var result = _service.FileExists(tempFile);
            await Assert.That(result).IsTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task FileExists_WithNonExistentFile_ReturnsFalse()
    {
        var result = _service.FileExists("/nonexistent/file.cs");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsCodeFile_WithNullPath_ReturnsFalse()
    {
        var result = _service.IsCodeFile(null!);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsCodeFile_WithEmptyPath_ReturnsFalse()
    {
        var result = _service.IsCodeFile("");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetPathInfo_WithEmptyPath_ReturnsExpectedDefaults()
    {
        var result = _service.GetPathInfo("");

        await Assert.That(result.FilePath).IsEqualTo("");
        await Assert.That(result.Exists).IsFalse();
        await Assert.That(result.IsWithinSolutionDirectory).IsFalse();
        await Assert.That(result.IsFormattable).IsFalse();
        await Assert.That(result.WriteRestrictionReason).IsEqualTo("Path is empty or null");
    }

    [Test]
    public async Task GetPathInfo_WithNullPath_ReturnsExpectedDefaults()
    {
        var result = _service.GetPathInfo(null!);

        await Assert.That(result.Exists).IsFalse();
        await Assert.That(result.IsWithinSolutionDirectory).IsFalse();
        await Assert.That(result.WriteRestrictionReason).IsEqualTo("Path is empty or null");
    }

    [Test]
    public async Task GetPathInfo_WithPathContainingUnsafeDirectory_SetsWriteRestriction()
    {
        _solutionManager.IsSolutionLoaded.Returns(false);

        var result = _service.GetPathInfo("/some/project/bin/file.dll");

        await Assert.That(result.WriteRestrictionReason).IsNotNull();
    }

    [Test]
    public async Task GetPathInfo_WithPathContainingGitDir_SetsWriteRestriction()
    {
        _solutionManager.IsSolutionLoaded.Returns(false);

        var result = _service.GetPathInfo("/some/project/.git/config");

        await Assert.That(result.WriteRestrictionReason).IsNotNull();
    }

    [Test]
    public async Task GetPathInfo_WithPathContainingObjDir_SetsWriteRestriction()
    {
        _solutionManager.IsSolutionLoaded.Returns(false);

        var result = _service.GetPathInfo("/some/project/obj/Debug/net8.0/file.dll");

        await Assert.That(result.WriteRestrictionReason).IsNotNull();
    }

    [Test]
    public async Task GetPathInfo_WithPathContainingNodeModules_SetsWriteRestriction()
    {
        _solutionManager.IsSolutionLoaded.Returns(false);

        var result = _service.GetPathInfo("/some/project/node_modules/package/index.js");

        await Assert.That(result.WriteRestrictionReason).IsNotNull();
    }

    [Test]
    public async Task GetPathInfo_WithPathOutsideSolution_IsNotWithinSolutionDirectory()
    {
        _solutionManager.IsSolutionLoaded.Returns(false);

        var result = _service.GetPathInfo("/outside/solution/file.cs");

        await Assert.That(result.IsWithinSolutionDirectory).IsFalse();
    }

    [Test]
    public async Task IsPathReadable_WhenSolutionNotLoaded_ReturnsFalse()
    {
        _solutionManager.IsSolutionLoaded.Returns(false);

        var result = _service.IsPathReadable("/some/file.cs");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPathWritable_WhenSolutionNotLoaded_ReturnsFalse()
    {
        _solutionManager.IsSolutionLoaded.Returns(false);

        var result = _service.IsPathWritable("/some/file.cs");

        await Assert.That(result).IsFalse();
    }
}
