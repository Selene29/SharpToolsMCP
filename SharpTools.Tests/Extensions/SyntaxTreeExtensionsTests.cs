using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SharpTools.Tools.Extensions;

namespace SharpTools.Tests.Extensions;

public class SyntaxTreeExtensionsTests
{
    [Test]
    public async Task GetRequiredProject_WhenFileNotInSolution_ThrowsInvalidOperationException()
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "TestProject",
            "TestProject",
            LanguageNames.CSharp);
        var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), null, new[] { projectInfo }));

        // Create a standalone syntax tree not in the solution
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class Standalone {}");

        await Assert.That(() => tree.GetRequiredProject(solution))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GetRequiredProject_WhenFileInSingleProject_ReturnsProject()
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "TestProject",
            "TestProject",
            LanguageNames.CSharp,
            documents: new[]
            {
                DocumentInfo.Create(
                    documentId,
                    "Test.cs",
                    loader: TextLoader.From(TextAndVersion.Create(
                        SourceText.From("class TestClass {}"),
                        VersionStamp.Create(),
                        "Test.cs")),
                    filePath: "Test.cs")
            });

        var solution = workspace.AddSolution(
            SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), null, new[] { projectInfo }));

        var document = solution.GetDocument(documentId)!;
        var tree = (await document.GetSyntaxTreeAsync())!;

        var project = tree.GetRequiredProject(solution);

        await Assert.That(project.Name).IsEqualTo("TestProject");
    }

    [Test]
    public async Task GetRequiredProject_WhenFileInMultipleProjects_ThrowsInvalidOperationException()
    {
        var workspace = new AdhocWorkspace();
        var projectId1 = ProjectId.CreateNewId();
        var projectId2 = ProjectId.CreateNewId();
        var documentId1 = DocumentId.CreateNewId(projectId1);
        var documentId2 = DocumentId.CreateNewId(projectId2);

        // Both projects have a document with the same file path
        var sharedFilePath = "/shared/Test.cs";

        var projectInfo1 = ProjectInfo.Create(
            projectId1,
            VersionStamp.Create(),
            "Project1",
            "Project1",
            LanguageNames.CSharp,
            documents: new[]
            {
                DocumentInfo.Create(documentId1, "Test.cs",
                    loader: TextLoader.From(TextAndVersion.Create(
                        SourceText.From("class TestClass {}"),
                        VersionStamp.Create(),
                        sharedFilePath)),
                    filePath: sharedFilePath)
            });

        var projectInfo2 = ProjectInfo.Create(
            projectId2,
            VersionStamp.Create(),
            "Project2",
            "Project2",
            LanguageNames.CSharp,
            documents: new[]
            {
                DocumentInfo.Create(documentId2, "Test.cs",
                    loader: TextLoader.From(TextAndVersion.Create(
                        SourceText.From("class TestClass {}"),
                        VersionStamp.Create(),
                        sharedFilePath)),
                    filePath: sharedFilePath)
            });

        var solution = workspace.AddSolution(
            SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), null,
                new[] { projectInfo1, projectInfo2 }));

        var document = solution.GetDocument(documentId1)!;
        var tree = (await document.GetSyntaxTreeAsync())!;

        await Assert.That(() => tree.GetRequiredProject(solution))
            .ThrowsExactly<InvalidOperationException>();
    }
}
