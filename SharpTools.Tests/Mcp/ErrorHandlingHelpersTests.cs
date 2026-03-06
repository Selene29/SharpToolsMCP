using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using NSubstitute;
using SharpTools.Tools.Mcp;

namespace SharpTools.Tests.Mcp;

public class ErrorHandlingHelpersTests
{
    [Test]
    public async Task ValidateStringParameter_WithNull_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger>();

        await Assert.That(() => ErrorHandlingHelpers.ValidateStringParameter(null, "testParam", logger))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ValidateStringParameter_WithEmptyString_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger>();

        await Assert.That(() => ErrorHandlingHelpers.ValidateStringParameter("", "testParam", logger))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ValidateStringParameter_WithWhitespace_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger>();

        await Assert.That(() => ErrorHandlingHelpers.ValidateStringParameter("   ", "testParam", logger))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ValidateStringParameter_WithValidString_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger>();

        ErrorHandlingHelpers.ValidateStringParameter("valid", "testParam", logger);

        // No exception should be thrown
        await Assert.That(logger.ReceivedCalls().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task ValidateFilePath_WithNull_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger>();

        await Assert.That(() => ErrorHandlingHelpers.ValidateFilePath(null, logger))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ValidateFilePath_WithEmptyString_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger>();

        await Assert.That(() => ErrorHandlingHelpers.ValidateFilePath("", logger))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ValidateFilePath_WithValidPath_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger>();

        ErrorHandlingHelpers.ValidateFilePath("/some/valid/path.cs", logger);

        // Should complete without exception - verify no error logging
        await Assert.That(logger.ReceivedCalls().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task ValidateFileExists_WithNonExistentFile_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger>();

        await Assert.That(() => ErrorHandlingHelpers.ValidateFileExists("/nonexistent/path/file.cs", logger))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ValidateFileExists_WithExistingFile_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger>();
        var tempFile = Path.GetTempFileName();

        try
        {
            ErrorHandlingHelpers.ValidateFileExists(tempFile, logger);
            // Should complete without exception - verify no error logging
            await Assert.That(logger.ReceivedCalls().Count()).IsEqualTo(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_SuccessfulOperation_ReturnsResult()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();

        var result = await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => Task.FromResult("success"),
            logger,
            "TestOperation",
            CancellationToken.None);

        await Assert.That(result).IsEqualTo("success");
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_WithCancellation_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => Task.FromResult("success"),
            logger,
            "TestOperation",
            cts.Token))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_WithArgumentException_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();

        await Assert.That(async () => await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => throw new ArgumentException("bad arg"),
            logger,
            "TestOperation",
            CancellationToken.None))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_WithFileNotFoundException_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();

        await Assert.That(async () => await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => throw new FileNotFoundException("not found"),
            logger,
            "TestOperation",
            CancellationToken.None))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_WithIOException_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();

        await Assert.That(async () => await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => throw new IOException("io error"),
            logger,
            "TestOperation",
            CancellationToken.None))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_WithUnauthorizedAccessException_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();

        await Assert.That(async () => await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => throw new UnauthorizedAccessException("access denied"),
            logger,
            "TestOperation",
            CancellationToken.None))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_WithGenericException_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();

        await Assert.That(async () => await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => throw new Exception("generic error"),
            logger,
            "TestOperation",
            CancellationToken.None))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_WithMcpException_RethrowsSameException()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();

        await Assert.That(async () => await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => throw new McpException("mcp error"),
            logger,
            "TestOperation",
            CancellationToken.None))
            .ThrowsExactly<McpException>();
    }

    [Test]
    public async Task ExecuteWithErrorHandlingAsync_WithInvalidOperationException_ThrowsMcpException()
    {
        var logger = Substitute.For<ILogger<ErrorHandlingHelpersTests>>();

        await Assert.That(async () => await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync<string, ErrorHandlingHelpersTests>(
            () => throw new InvalidOperationException("invalid op"),
            logger,
            "TestOperation",
            CancellationToken.None))
            .ThrowsExactly<McpException>();
    }
}
