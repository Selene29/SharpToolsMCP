using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpTools.Tools.Extensions;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Services;

namespace SharpTools.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public async Task WithSharpToolsServices_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.WithSharpToolsServices();

        var serviceProvider = services.BuildServiceProvider();

        await Assert.That(serviceProvider.GetService<ISolutionManager>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<ICodeAnalysisService>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<IGitService>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<ICodeModificationService>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<IEditorConfigProvider>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<IDocumentOperationsService>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<IComplexityAnalysisService>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<ISemanticSimilarityService>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<ISourceResolutionService>()).IsNotNull();
        await Assert.That(serviceProvider.GetService<IFuzzyFqnLookupService>()).IsNotNull();
    }

    [Test]
    public async Task WithSharpToolsServices_GitEnabled_RegistersGitService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.WithSharpToolsServices(enableGit: true);

        var serviceProvider = services.BuildServiceProvider();
        var gitService = serviceProvider.GetService<IGitService>();

        await Assert.That(gitService).IsNotNull();
        await Assert.That(gitService).IsTypeOf<GitService>();
    }

    [Test]
    public async Task WithSharpToolsServices_GitDisabled_RegistersNoOpGitService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.WithSharpToolsServices(enableGit: false);

        var serviceProvider = services.BuildServiceProvider();
        var gitService = serviceProvider.GetService<IGitService>();

        await Assert.That(gitService).IsNotNull();
        await Assert.That(gitService).IsTypeOf<NoOpGitService>();
    }

    [Test]
    public async Task WithSharpToolsServices_ServicesAreSingletons()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.WithSharpToolsServices();

        var serviceProvider = services.BuildServiceProvider();

        var solutionManager1 = serviceProvider.GetService<ISolutionManager>();
        var solutionManager2 = serviceProvider.GetService<ISolutionManager>();

        await Assert.That(solutionManager1).IsNotNull();
        await Assert.That(solutionManager1).IsSameReferenceAs(solutionManager2!);
    }
}
