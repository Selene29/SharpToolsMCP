using SharpTools.Tools.Services;

namespace SharpTools.Tests.Services;

public class DataModelTests
{
    [Test]
    public async Task MethodSemanticFeatures_StoresAllProperties()
    {
        var features = new MethodSemanticFeatures(
            fullyQualifiedMethodName: "Namespace.Class.Method",
            filePath: "/path/to/file.cs",
            startLine: 10,
            methodName: "Method",
            returnTypeName: "void",
            parameterTypeNames: new List<string> { "int", "string" },
            invokedMethodSignatures: new HashSet<string> { "Console.WriteLine" },
            basicBlockCount: 3,
            conditionalBranchCount: 1,
            loopCount: 2,
            cyclomaticComplexity: 5,
            operationCounts: new Dictionary<string, int> { { "Invocation", 3 } },
            distinctAccessedMemberTypes: new HashSet<string> { "System.String" }
        );

        await Assert.That(features.FullyQualifiedMethodName).IsEqualTo("Namespace.Class.Method");
        await Assert.That(features.FilePath).IsEqualTo("/path/to/file.cs");
        await Assert.That(features.StartLine).IsEqualTo(10);
        await Assert.That(features.MethodName).IsEqualTo("Method");
        await Assert.That(features.ReturnTypeName).IsEqualTo("void");
        await Assert.That(features.ParameterTypeNames.Count).IsEqualTo(2);
        await Assert.That(features.InvokedMethodSignatures.Count).IsEqualTo(1);
        await Assert.That(features.BasicBlockCount).IsEqualTo(3);
        await Assert.That(features.ConditionalBranchCount).IsEqualTo(1);
        await Assert.That(features.LoopCount).IsEqualTo(2);
        await Assert.That(features.CyclomaticComplexity).IsEqualTo(5);
        await Assert.That(features.OperationCounts.Count).IsEqualTo(1);
        await Assert.That(features.DistinctAccessedMemberTypes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MethodSimilarityResult_StoresProperties()
    {
        var methods = new List<MethodSemanticFeatures>
        {
            new MethodSemanticFeatures(
                "Ns.C.M1", "/file.cs", 1, "M1", "void",
                new List<string>(), new HashSet<string>(),
                1, 0, 0, 1,
                new Dictionary<string, int>(), new HashSet<string>()
            ),
            new MethodSemanticFeatures(
                "Ns.C.M2", "/file.cs", 10, "M2", "void",
                new List<string>(), new HashSet<string>(),
                1, 0, 0, 1,
                new Dictionary<string, int>(), new HashSet<string>()
            )
        };

        var result = new MethodSimilarityResult(methods, 0.95);

        await Assert.That(result.SimilarMethods.Count).IsEqualTo(2);
        await Assert.That(result.AverageSimilarityScore).IsEqualTo(0.95);
    }

    [Test]
    public async Task ClassSemanticFeatures_StoresAllProperties()
    {
        var methodFeatures = new List<MethodSemanticFeatures>();
        var features = new ClassSemanticFeatures(
            FullyQualifiedClassName: "Namespace.TestClass",
            FilePath: "/path/to/file.cs",
            StartLine: 1,
            ClassName: "TestClass",
            BaseClassName: "BaseClass",
            ImplementedInterfaceNames: new List<string> { "IInterface1" },
            PublicMethodCount: 5,
            ProtectedMethodCount: 2,
            PrivateMethodCount: 3,
            StaticMethodCount: 1,
            AbstractMethodCount: 0,
            VirtualMethodCount: 1,
            PropertyCount: 4,
            ReadOnlyPropertyCount: 2,
            StaticPropertyCount: 0,
            FieldCount: 3,
            StaticFieldCount: 1,
            ReadonlyFieldCount: 2,
            ConstFieldCount: 1,
            EventCount: 0,
            NestedClassCount: 0,
            NestedStructCount: 0,
            NestedEnumCount: 1,
            NestedInterfaceCount: 0,
            AverageMethodComplexity: 3.5,
            DistinctReferencedExternalTypeFqns: new HashSet<string> { "System.String" },
            DistinctUsedNamespaceFqns: new HashSet<string> { "System" },
            TotalLinesOfCode: 100,
            MethodFeatures: methodFeatures
        );

        await Assert.That(features.FullyQualifiedClassName).IsEqualTo("Namespace.TestClass");
        await Assert.That(features.ClassName).IsEqualTo("TestClass");
        await Assert.That(features.BaseClassName).IsEqualTo("BaseClass");
        await Assert.That(features.PublicMethodCount).IsEqualTo(5);
        await Assert.That(features.ProtectedMethodCount).IsEqualTo(2);
        await Assert.That(features.PrivateMethodCount).IsEqualTo(3);
        await Assert.That(features.PropertyCount).IsEqualTo(4);
        await Assert.That(features.FieldCount).IsEqualTo(3);
        await Assert.That(features.AverageMethodComplexity).IsEqualTo(3.5);
        await Assert.That(features.TotalLinesOfCode).IsEqualTo(100);
        await Assert.That(features.ImplementedInterfaceNames.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ClassSimilarityResult_StoresProperties()
    {
        var classes = new List<ClassSemanticFeatures>
        {
            new ClassSemanticFeatures(
                "Ns.C1", "/file.cs", 1, "C1", null,
                new List<string>(), 1, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                1.0, new HashSet<string>(), new HashSet<string>(),
                10, new List<MethodSemanticFeatures>()
            )
        };

        var result = new ClassSimilarityResult(classes, 0.85);

        await Assert.That(result.SimilarClasses.Count).IsEqualTo(1);
        await Assert.That(result.AverageSimilarityScore).IsEqualTo(0.85);
    }

    [Test]
    public async Task ClassSemanticFeatures_WithNullBaseClass_StoresNull()
    {
        var features = new ClassSemanticFeatures(
            FullyQualifiedClassName: "Namespace.TestClass",
            FilePath: "/path/to/file.cs",
            StartLine: 1,
            ClassName: "TestClass",
            BaseClassName: null,
            ImplementedInterfaceNames: new List<string>(),
            PublicMethodCount: 0, ProtectedMethodCount: 0, PrivateMethodCount: 0,
            StaticMethodCount: 0, AbstractMethodCount: 0, VirtualMethodCount: 0,
            PropertyCount: 0, ReadOnlyPropertyCount: 0, StaticPropertyCount: 0,
            FieldCount: 0, StaticFieldCount: 0, ReadonlyFieldCount: 0, ConstFieldCount: 0,
            EventCount: 0, NestedClassCount: 0, NestedStructCount: 0,
            NestedEnumCount: 0, NestedInterfaceCount: 0,
            AverageMethodComplexity: 0,
            DistinctReferencedExternalTypeFqns: new HashSet<string>(),
            DistinctUsedNamespaceFqns: new HashSet<string>(),
            TotalLinesOfCode: 0,
            MethodFeatures: new List<MethodSemanticFeatures>()
        );

        await Assert.That(features.BaseClassName).IsNull();
    }
}
