using SharpTools.Tools.Services;

namespace SharpTools.Tests.Services;

public class PathInfoTests
{
    [Test]
    public async Task IsReadable_WhenExistsAndWithinSolution_ReturnsTrue()
    {
        var pathInfo = new PathInfo
        {
            FilePath = "/some/file.cs",
            Exists = true,
            IsWithinSolutionDirectory = true,
            IsReferencedBySolution = false,
            IsFormattable = true
        };

        await Assert.That(pathInfo.IsReadable).IsTrue();
    }

    [Test]
    public async Task IsReadable_WhenExistsAndReferencedBySolution_ReturnsTrue()
    {
        var pathInfo = new PathInfo
        {
            FilePath = "/some/file.cs",
            Exists = true,
            IsWithinSolutionDirectory = false,
            IsReferencedBySolution = true,
            IsFormattable = true
        };

        await Assert.That(pathInfo.IsReadable).IsTrue();
    }

    [Test]
    public async Task IsReadable_WhenNotExists_ReturnsFalse()
    {
        var pathInfo = new PathInfo
        {
            FilePath = "/some/file.cs",
            Exists = false,
            IsWithinSolutionDirectory = true,
            IsReferencedBySolution = true,
            IsFormattable = true
        };

        await Assert.That(pathInfo.IsReadable).IsFalse();
    }

    [Test]
    public async Task IsReadable_WhenExistsButNotInSolutionAndNotReferenced_ReturnsFalse()
    {
        var pathInfo = new PathInfo
        {
            FilePath = "/some/file.cs",
            Exists = true,
            IsWithinSolutionDirectory = false,
            IsReferencedBySolution = false,
            IsFormattable = true
        };

        await Assert.That(pathInfo.IsReadable).IsFalse();
    }

    [Test]
    public async Task IsWritable_WhenWithinSolutionAndNoRestriction_ReturnsTrue()
    {
        var pathInfo = new PathInfo
        {
            FilePath = "/some/file.cs",
            Exists = true,
            IsWithinSolutionDirectory = true,
            IsReferencedBySolution = true,
            IsFormattable = true,
            WriteRestrictionReason = null
        };

        await Assert.That(pathInfo.IsWritable).IsTrue();
    }

    [Test]
    public async Task IsWritable_WhenWithinSolutionAndEmptyRestriction_ReturnsTrue()
    {
        var pathInfo = new PathInfo
        {
            FilePath = "/some/file.cs",
            Exists = true,
            IsWithinSolutionDirectory = true,
            IsReferencedBySolution = true,
            IsFormattable = true,
            WriteRestrictionReason = ""
        };

        await Assert.That(pathInfo.IsWritable).IsTrue();
    }

    [Test]
    public async Task IsWritable_WhenNotWithinSolution_ReturnsFalse()
    {
        var pathInfo = new PathInfo
        {
            FilePath = "/some/file.cs",
            Exists = true,
            IsWithinSolutionDirectory = false,
            IsReferencedBySolution = true,
            IsFormattable = true,
            WriteRestrictionReason = null
        };

        await Assert.That(pathInfo.IsWritable).IsFalse();
    }

    [Test]
    public async Task IsWritable_WhenRestrictionReasonSet_ReturnsFalse()
    {
        var pathInfo = new PathInfo
        {
            FilePath = "/some/file.cs",
            Exists = true,
            IsWithinSolutionDirectory = true,
            IsReferencedBySolution = true,
            IsFormattable = true,
            WriteRestrictionReason = "Path contains a protected directory"
        };

        await Assert.That(pathInfo.IsWritable).IsFalse();
    }

    [Test]
    public async Task DefaultPathInfo_HasExpectedDefaults()
    {
        var pathInfo = new PathInfo();

        await Assert.That(pathInfo.FilePath).IsNull();
        await Assert.That(pathInfo.Exists).IsFalse();
        await Assert.That(pathInfo.IsWithinSolutionDirectory).IsFalse();
        await Assert.That(pathInfo.IsReferencedBySolution).IsFalse();
        await Assert.That(pathInfo.IsFormattable).IsFalse();
        await Assert.That(pathInfo.ProjectId).IsNull();
        await Assert.That(pathInfo.WriteRestrictionReason).IsNull();
        await Assert.That(pathInfo.IsReadable).IsFalse();
        await Assert.That(pathInfo.IsWritable).IsFalse();
    }
}
