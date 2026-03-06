using SharpTools.Tools.Services;

namespace SharpTools.Tests.Services;

public class LegacyNuGetPackageReaderTests
{
    [Test]
    public async Task GetPackagesConfigPath_WithValidProjectPath_ReturnsPackagesConfigPath()
    {
        var projectPath = Path.Combine("/some", "project", "MyProject.csproj");
        var result = LegacyNuGetPackageReader.GetPackagesConfigPath(projectPath);

        var expected = Path.Combine("/some", "project", "packages.config");
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task GetPackagesConfigPath_WithEmptyPath_ReturnsEmpty()
    {
        var result = LegacyNuGetPackageReader.GetPackagesConfigPath(string.Empty);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetPackagesConfigPath_WithNull_ReturnsEmpty()
    {
        var result = LegacyNuGetPackageReader.GetPackagesConfigPath(null!);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DetectPackageFormat_WithNonExistentPath_ReturnsPackageReference()
    {
        var result = LegacyNuGetPackageReader.DetectPackageFormat("/nonexistent/path/Project.csproj");

        await Assert.That(result).IsEqualTo(LegacyNuGetPackageReader.PackageFormat.PackageReference);
    }

    [Test]
    public async Task DetectPackageFormat_WithEmptyPath_ReturnsPackageReference()
    {
        var result = LegacyNuGetPackageReader.DetectPackageFormat("");

        await Assert.That(result).IsEqualTo(LegacyNuGetPackageReader.PackageFormat.PackageReference);
    }

    [Test]
    public async Task DetectPackageFormat_WithSdkStyleProject_ReturnsPackageReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        File.WriteAllText(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
</Project>");

        try
        {
            var result = LegacyNuGetPackageReader.DetectPackageFormat(projectPath);
            await Assert.That(result).IsEqualTo(LegacyNuGetPackageReader.PackageFormat.PackageReference);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task DetectPackageFormat_WithPackagesConfig_ReturnsPackagesConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        File.WriteAllText(projectPath, @"<Project><PropertyGroup></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(tempDir, "packages.config"),
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<packages>
  <package id=""Newtonsoft.Json"" version=""13.0.3"" targetFramework=""net48"" />
</packages>");

        try
        {
            var result = LegacyNuGetPackageReader.DetectPackageFormat(projectPath);
            await Assert.That(result).IsEqualTo(LegacyNuGetPackageReader.PackageFormat.PackagesConfig);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task GetBasicPackageReferencesWithoutMSBuild_WithValidProject_ReturnsPackages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        File.WriteAllText(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
    <PackageReference Include=""Serilog"" Version=""3.1.1"" />
  </ItemGroup>
</Project>");

        try
        {
            var packages = LegacyNuGetPackageReader.GetBasicPackageReferencesWithoutMSBuild(projectPath);

            await Assert.That(packages.Count).IsEqualTo(2);
            await Assert.That(packages.Any(p => p.PackageId == "Newtonsoft.Json" && p.Version == "13.0.3")).IsTrue();
            await Assert.That(packages.Any(p => p.PackageId == "Serilog" && p.Version == "3.1.1")).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task GetBasicPackageReferencesWithoutMSBuild_WithNonExistentFile_ReturnsEmpty()
    {
        var packages = LegacyNuGetPackageReader.GetBasicPackageReferencesWithoutMSBuild("/nonexistent.csproj");

        await Assert.That(packages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetBasicPackageReferencesWithoutMSBuild_WithVersionInElement_ReturnsPackages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        File.WriteAllText(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""MyPackage"">
      <Version>1.2.3</Version>
    </PackageReference>
  </ItemGroup>
</Project>");

        try
        {
            var packages = LegacyNuGetPackageReader.GetBasicPackageReferencesWithoutMSBuild(projectPath);

            await Assert.That(packages.Count).IsEqualTo(1);
            await Assert.That(packages[0].PackageId).IsEqualTo("MyPackage");
            await Assert.That(packages[0].Version).IsEqualTo("1.2.3");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task GetPackagesFromConfig_WithValidConfig_ReturnsPackages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "packages.config");
        File.WriteAllText(configPath,
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<packages>
  <package id=""Newtonsoft.Json"" version=""13.0.3"" targetFramework=""net48"" />
  <package id=""log4net"" version=""2.0.15"" targetFramework=""net48"" developmentDependency=""true"" />
</packages>");

        try
        {
            var packages = LegacyNuGetPackageReader.GetPackagesFromConfig(configPath);

            await Assert.That(packages.Count).IsEqualTo(2);
            await Assert.That(packages[0].PackageId).IsEqualTo("Newtonsoft.Json");
            await Assert.That(packages[0].Version).IsEqualTo("13.0.3");
            await Assert.That(packages[0].TargetFramework).IsEqualTo("net48");
            await Assert.That(packages[0].IsDevelopmentDependency).IsFalse();
            await Assert.That(packages[1].IsDevelopmentDependency).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task GetPackagesFromConfig_WithNonExistentFile_ReturnsEmpty()
    {
        var packages = LegacyNuGetPackageReader.GetPackagesFromConfig("/nonexistent/packages.config");

        await Assert.That(packages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPackagesFromConfig_WithNull_ReturnsEmpty()
    {
        var packages = LegacyNuGetPackageReader.GetPackagesFromConfig(null!);

        await Assert.That(packages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetAllPackages_WithNonExistentFile_ReturnsEmpty()
    {
        var packages = LegacyNuGetPackageReader.GetAllPackages("/nonexistent.csproj");

        await Assert.That(packages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetAllPackages_WithEmptyPath_ReturnsEmpty()
    {
        var packages = LegacyNuGetPackageReader.GetAllPackages("");

        await Assert.That(packages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetAllPackageReferences_WithNonExistentFile_ReturnsEmpty()
    {
        var packages = LegacyNuGetPackageReader.GetAllPackageReferences("/nonexistent.csproj");

        await Assert.That(packages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetAllPackageReferences_WithEmptyPath_ReturnsEmpty()
    {
        var packages = LegacyNuGetPackageReader.GetAllPackageReferences("");

        await Assert.That(packages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPackagesForProject_WithSdkStyleProject_ReturnsPackages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        File.WriteAllText(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""TestPkg"" Version=""1.0.0"" />
  </ItemGroup>
</Project>");

        try
        {
            var info = LegacyNuGetPackageReader.GetPackagesForProject(projectPath);

            await Assert.That(info.Format).IsEqualTo(LegacyNuGetPackageReader.PackageFormat.PackageReference);
            await Assert.That(info.Packages.Count).IsEqualTo(1);
            await Assert.That(info.ProjectPath).IsEqualTo(projectPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task PackageReference_DefaultValues_AreCorrect()
    {
        var pkg = new LegacyNuGetPackageReader.PackageReference();

        await Assert.That(pkg.PackageId).IsEqualTo(string.Empty);
        await Assert.That(pkg.Version).IsEqualTo(string.Empty);
        await Assert.That(pkg.TargetFramework).IsNull();
        await Assert.That(pkg.IsDevelopmentDependency).IsFalse();
        await Assert.That(pkg.HintPath).IsNull();
    }
}
