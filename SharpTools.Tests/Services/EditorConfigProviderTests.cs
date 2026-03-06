using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpTools.Tools.Services;

namespace SharpTools.Tests.Services;

public class EditorConfigProviderTests
{
    private ILogger<EditorConfigProvider> _logger = null!;
    private EditorConfigProvider _provider = null!;

    [Before(Test)]
    public void Setup()
    {
        _logger = Substitute.For<ILogger<EditorConfigProvider>>();
        _provider = new EditorConfigProvider(_logger);
    }

    [Test]
    public async Task GetRootEditorConfigPath_BeforeInitialize_ReturnsNull()
    {
        var result = _provider.GetRootEditorConfigPath();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task InitializeAsync_WithNullDirectory_ThrowsArgumentNullException()
    {
        await Assert.That(() => _provider.InitializeAsync(null!, CancellationToken.None))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task InitializeAsync_WithDirectoryContainingEditorConfig_FindsConfig()
    {
        // Create a temp directory with .editorconfig
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var editorConfigPath = Path.Combine(tempDir, ".editorconfig");
        File.WriteAllText(editorConfigPath, "root = true\n");

        try
        {
            await _provider.InitializeAsync(tempDir, CancellationToken.None);
            var result = _provider.GetRootEditorConfigPath();

            await Assert.That(result).IsEqualTo(editorConfigPath);
        }
        finally
        {
            // Cleanup
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task InitializeAsync_WithDirectoryWithoutEditorConfig_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        // Also create a .git directory so it stops searching at this level
        Directory.CreateDirectory(Path.Combine(tempDir, ".git"));

        try
        {
            await _provider.InitializeAsync(tempDir, CancellationToken.None);
            var result = _provider.GetRootEditorConfigPath();

            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task InitializeAsync_WithEditorConfigInParentDirectory_FindsConfig()
    {
        // Create parent dir with .editorconfig and .git
        var parentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(parentDir);
        Directory.CreateDirectory(Path.Combine(parentDir, ".git"));
        var editorConfigPath = Path.Combine(parentDir, ".editorconfig");
        File.WriteAllText(editorConfigPath, "root = true\n");

        // Create child directory
        var childDir = Path.Combine(parentDir, "subdir");
        Directory.CreateDirectory(childDir);

        try
        {
            await _provider.InitializeAsync(childDir, CancellationToken.None);
            var result = _provider.GetRootEditorConfigPath();

            await Assert.That(result).IsEqualTo(editorConfigPath);
        }
        finally
        {
            // Cleanup
            Directory.Delete(parentDir, true);
        }
    }

    [Test]
    public async Task Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        await Assert.That(() => new EditorConfigProvider(null!))
            .ThrowsExactly<ArgumentNullException>();
    }
}
