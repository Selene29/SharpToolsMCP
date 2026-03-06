using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Mcp;
using SharpTools.Tools.Services;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBSyntaxFactory = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory;
using VisualBasicSyntaxTree = Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree;

namespace SharpTools.Tools.Mcp.Tools;

// Marker class for ILogger<T> category specific to ModificationTools
public class ModificationToolsLogCategory { }

[McpServerToolType]
public static class ModificationTools {
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(AddMember), Idempotent = false, Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("Adds one or more new member definitions (Property, Field, Method, inner Class, etc.) to a specified type. Code is parsed, inserted, and formatted. Definition can include xml documentation and attributes. Writing small components produces cleaner code, so you can use this to break up large components, in addition to adding new functionality. Supports C# and VB.NET.")]
    public static async Task<string> AddMember(
        ISolutionManager solutionManager,
        ICodeModificationService modificationService,
        IComplexityAnalysisService complexityAnalysisService,
        ISemanticSimilarityService semanticSimilarityService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("FQN of the parent type or method.")] string fullyQualifiedTargetName,
        [Description("The code to add (C# or VB.NET, matching the target file's language).")] string codeSnippet,
        [Description("If the target is a partial type, specifies which file to add to. Set to 'auto' to determine automatically.")] string fileNameHint,
        [Description("Suggest a line number to insert the member near. '-1' to determine automatically.")] int lineNumberHint,
        string commitMessage,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedTargetName, "fullyQualifiedTargetName", logger);
            ErrorHandlingHelpers.ValidateStringParameter(codeSnippet, "codeSnippet", logger);
            codeSnippet = codeSnippet.TrimBackslash();

            // Ensure solution is loaded
            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(AddMember));
            logger.LogInformation("Executing '{AddMember}' for target: {TargetName}", nameof(AddMember), fullyQualifiedTargetName);

            // Get the target symbol
            var targetSymbol = await ToolHelpers.GetRoslynSymbolOrThrowAsync(solutionManager, fullyQualifiedTargetName, cancellationToken);

            SyntaxReference? targetSyntaxRef = null;
            if (!string.IsNullOrEmpty(fileNameHint) && fileNameHint != "auto") {
                targetSyntaxRef = targetSymbol.DeclaringSyntaxReferences.FirstOrDefault(sr =>
                    sr.SyntaxTree.FilePath != null && sr.SyntaxTree.FilePath.Contains(fileNameHint));

                if (targetSyntaxRef == null) {
                    throw new McpException($"File hint '{fileNameHint}' did not match any declaring syntax reference for symbol '{fullyQualifiedTargetName}'.");
                }
            } else {
                targetSyntaxRef = targetSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            }
            if (targetSyntaxRef == null) {
                throw new McpException($"Could not find a suitable syntax reference for symbol '{fullyQualifiedTargetName}'.");
            }

            if (solutionManager.CurrentSolution == null) {
                throw new McpException("Current solution is unexpectedly null after validation checks.");
            }

            var syntaxNode = await targetSyntaxRef.GetSyntaxAsync(cancellationToken);
            var document = ToolHelpers.GetDocumentFromSyntaxNodeOrThrow(solutionManager.CurrentSolution, syntaxNode);

            if (targetSymbol is not INamedTypeSymbol typeSymbol) {
                throw new McpException($"Target '{fullyQualifiedTargetName}' is not a type, cannot add member.");
            }

            // Parse the code snippet – use the parser matching the document's language
            MemberDeclarationSyntax? memberSyntax;
            var documentLanguage = document.Project.Language;
            if (documentLanguage == LanguageNames.VisualBasic) {
                // VB.NET: use a language-agnostic editor path; AddMemberAsync is C#-only,
                // so we apply the change inline here for VB projects.
                return await AddMemberVBAsync(solutionManager, modificationService, complexityAnalysisService,
                    semanticSimilarityService, logger, fullyQualifiedTargetName, codeSnippet,
                    document, typeSymbol, lineNumberHint, commitMessage, cancellationToken);
            }
            try {
                memberSyntax = SyntaxFactory.ParseMemberDeclaration(codeSnippet);
                if (memberSyntax == null) {
                    throw new McpException("Failed to parse code snippet as a valid member declaration.");
                }
            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Failed to parse code snippet as member declaration");
                throw new McpException($"Invalid C# syntax in code snippet: {ex.Message}");
            }

            // Verify the member name doesn't already exist in the type
            string memberName = GetMemberName(memberSyntax);
            logger.LogInformation("Adding member with name: {MemberName}", memberName);

            // Check for duplicate members
            if (!IsMemberAllowed(typeSymbol, memberSyntax, memberName, cancellationToken)) {
                throw new McpException($"A member with the name '{memberName}' already exists in '{fullyQualifiedTargetName}'" +
                    (memberSyntax is MethodDeclarationSyntax ? " with the same parameter signature." : "."));
            }

            try {
                // Use the lineNumberHint parameter when calling AddMemberAsync
                var newSolution = await modificationService.AddMemberAsync(document.Id, typeSymbol, memberSyntax, lineNumberHint, cancellationToken);

                string finalCommitMessage = $"Add {memberName} to {typeSymbol.Name}: " + commitMessage;
                await modificationService.ApplyChangesAsync(newSolution, cancellationToken, finalCommitMessage);

                // Check for compilation errors after adding the code
                var updatedDocument = solutionManager.CurrentSolution.GetDocument(document.Id);
                if (updatedDocument is null) {
                    logger.LogError("Updated document for {TargetName} is null after applying changes", fullyQualifiedTargetName);
                    throw new McpException($"Failed to retrieve updated document for {fullyQualifiedTargetName} after applying changes.");
                }
                var (hasErrors, errorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(
                    solutionManager, updatedDocument, logger, cancellationToken);

                // Perform complexity and similarity analysis on the added member
                string analysisResults = string.Empty;

                // Get the updated type symbol to find the newly added member
                var updatedSemanticModel = await updatedDocument.GetSemanticModelAsync(cancellationToken);
                if (updatedSemanticModel != null) {
                    // Find the type symbol in the updated document by FQN instead of using old syntax reference
                    var updatedTypeSymbol = await ToolHelpers.GetRoslynSymbolOrThrowAsync(solutionManager, fullyQualifiedTargetName, cancellationToken) as INamedTypeSymbol;

                    if (updatedTypeSymbol != null) {
                        // Find the added member by name
                        var addedSymbol = updatedTypeSymbol.GetMembers(memberName).FirstOrDefault();
                        if (addedSymbol != null) {
                            analysisResults = await MemberAnalysisHelper.AnalyzeAddedMemberAsync(
                                addedSymbol, complexityAnalysisService, semanticSimilarityService, logger, cancellationToken);
                        }
                    }
                }

                string baseMessage = $"Successfully added member to {fullyQualifiedTargetName} in {document.FilePath ?? "unknown file"}.\n\n" +
                    ((!hasErrors) ? "<errorCheck>No compilation issues detected.</errorCheck>" :
                    ($"{errorMessages}\n" + //Code added is not necessary in Copilot, as it can see the invocation  $"\nCode added:\n{codeSnippet}\n\n" +
                    $"If you choose to fix these issues, you must use {ToolHelpers.SharpToolPrefix + nameof(OverwriteMember)} to replace the member with a new definition."));

                return string.IsNullOrWhiteSpace(analysisResults) ? baseMessage : $"{baseMessage}\n\n{analysisResults}";
            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Failed to add member to {TypeName}", fullyQualifiedTargetName);
                throw new McpException($"Failed to add member to {fullyQualifiedTargetName}: {ex.Message}");
            }
        }, logger, nameof(AddMember), cancellationToken);
    }
    private static string GetMemberName(MemberDeclarationSyntax memberSyntax) {
        return memberSyntax switch {
            MethodDeclarationSyntax method => method.Identifier.Text,
            ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
            DestructorDeclarationSyntax dtor => dtor.Identifier.Text,
            OperatorDeclarationSyntax op => op.OperatorToken.Text,
            ConversionOperatorDeclarationSyntax conv => conv.Type.ToString(),
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.Text,
            EnumDeclarationSyntax enumDecl => enumDecl.Identifier.Text,
            TypeDeclarationSyntax type => type.Identifier.Text,
            DelegateDeclarationSyntax del => del.Identifier.Text,
            EventDeclarationSyntax evt => evt.Identifier.Text,
            EventFieldDeclarationSyntax evtField => evtField.Declaration.Variables.First().Identifier.Text,
            IndexerDeclarationSyntax indexer => "this[]", // Indexers don't have names but use the 'this' keyword
            _ => throw new NotSupportedException($"Unsupported member type: {memberSyntax.GetType().Name}")
        };
    }
    // Helper method to check if the member is allowed to be added
    private static bool IsMemberAllowed(INamedTypeSymbol typeSymbol, MemberDeclarationSyntax newMember, string memberName, CancellationToken cancellationToken) {
        // Special handling for method overloads
        if (newMember is MethodDeclarationSyntax newMethod) {
            // Get all existing methods with the same name
            var existingMethods = typeSymbol.GetMembers(memberName)
                .OfType<IMethodSymbol>()
                .Where(m => !m.IsImplicitlyDeclared && m.MethodKind == MethodKind.Ordinary)
                .ToList();

            if (!existingMethods.Any()) {
                return true; // No method with the same name exists
            }

            // Convert parameters of the new method to comparable format
            var newMethodParams = newMethod.ParameterList.Parameters
                .Select(p => new {
                    Type = p.Type?.ToString() ?? "unknown",
                    IsRef = p.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword)),
                    IsOut = p.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword))
                })
                .ToList();

            // Check if any existing method has the same parameter signature
            foreach (var existingMethod in existingMethods) {
                if (existingMethod.Parameters.Length != newMethodParams.Count) {
                    continue; // Different parameter count, not a duplicate
                }

                bool signatureMatches = true;
                for (int i = 0; i < existingMethod.Parameters.Length; i++) {
                    var existingParam = existingMethod.Parameters[i];
                    var newParam = newMethodParams[i];

                    // Compare parameter types and ref/out modifiers
                    if (existingParam.Type.ToDisplayString() != newParam.Type ||
                        existingParam.RefKind == RefKind.Ref != newParam.IsRef ||
                        existingParam.RefKind == RefKind.Out != newParam.IsOut) {
                        signatureMatches = false;
                        break;
                    }
                }

                if (signatureMatches) {
                    return false; // Found a method with the same signature
                }
            }

            return true; // No matching signature found
        } else {
            // For non-method members, simply check if a member with the same name exists
            return !typeSymbol.GetMembers(memberName).Any(m => !m.IsImplicitlyDeclared);
        }
    }
    /// <summary>
    /// Adds a member to a VB.NET type using VB-specific syntax parsing and a language-agnostic editor.
    /// </summary>
    private static async Task<string> AddMemberVBAsync(
        ISolutionManager solutionManager,
        ICodeModificationService modificationService,
        IComplexityAnalysisService complexityAnalysisService,
        ISemanticSimilarityService semanticSimilarityService,
        ILogger<ModificationToolsLogCategory> logger,
        string fullyQualifiedTargetName,
        string codeSnippet,
        Document document,
        INamedTypeSymbol typeSymbol,
        int lineNumberHint,
        string commitMessage,
        CancellationToken cancellationToken) {
        // Parse the VB.NET code snippet
        var vbTree = VisualBasicSyntaxTree.ParseText(codeSnippet);
        var vbRoot = vbTree.GetRoot(cancellationToken) as VBSyntax.CompilationUnitSyntax;
        var newMember = vbRoot?.Members.FirstOrDefault();
        if (newMember == null) {
            throw new McpException("Failed to parse VB.NET code snippet as a valid member declaration.");
        }

        // Find the type declaration node in the document
        var typeDeclarationSyntaxRef = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault(sr =>
            sr.SyntaxTree.FilePath == document.FilePath);
        typeDeclarationSyntaxRef ??= typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (typeDeclarationSyntaxRef == null) {
            throw new McpException($"Could not find syntax reference for type '{fullyQualifiedTargetName}'.");
        }
        var typeDeclarationNode = await typeDeclarationSyntaxRef.GetSyntaxAsync(cancellationToken);

        // Use a language-agnostic document editor to insert the member
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken);
        editor.AddMember(typeDeclarationNode, newMember);

        var changedDocument = editor.GetChangedDocument();
        var formattedDocument = await modificationService.FormatDocumentAsync(changedDocument, cancellationToken);
        var newSolution = formattedDocument.Project.Solution;

        string memberName = "UnknownMember";
        // Try to extract a meaningful name
        if (newMember is VBSyntax.MethodBlockSyntax methodBlock)
            memberName = methodBlock.SubOrFunctionStatement.Identifier.Text;
        else if (newMember is VBSyntax.PropertyBlockSyntax propBlock)
            memberName = propBlock.PropertyStatement.Identifier.Text;
        else if (newMember is VBSyntax.FieldDeclarationSyntax fieldDecl)
            memberName = fieldDecl.Declarators.FirstOrDefault()?.Names.FirstOrDefault()?.Identifier.Text ?? memberName;
        else if (newMember is VBSyntax.ClassBlockSyntax classBlock)
            memberName = classBlock.ClassStatement.Identifier.Text;

        string finalCommitMessage = $"Add {memberName} to {typeSymbol.Name}: " + commitMessage;
        await modificationService.ApplyChangesAsync(newSolution, cancellationToken, finalCommitMessage);

        var updatedDocument = solutionManager.CurrentSolution?.GetDocument(document.Id);
        if (updatedDocument is null) {
            throw new McpException($"Failed to retrieve updated document for {fullyQualifiedTargetName} after applying changes.");
        }
        var (hasErrors, errorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(
            solutionManager, updatedDocument, logger, cancellationToken);

        string baseMessage = $"Successfully added member to {fullyQualifiedTargetName} in {document.FilePath ?? "unknown file"}.\n\n" +
            ((!hasErrors) ? "<errorCheck>No compilation issues detected.</errorCheck>" :
            ($"{errorMessages}\n" +
            $"If you choose to fix these issues, you must use {ToolHelpers.SharpToolPrefix + nameof(OverwriteMember)} to replace the member with a new definition."));
        return baseMessage;
    }
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(OverwriteMember), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("Replaces the definition of an existing member or type with new code, or deletes it. Code is parsed and formatted. Code can contain multiple new members, update the existing member, and/or replace it with a new one. Supports C# and VB.NET.")]
    public static async Task<string> OverwriteMember(
        ISolutionManager solutionManager,
        ICodeModificationService modificationService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("FQN of the member or type to rewrite.")] string fullyQualifiedMemberName,
        [Description("The new code for the member or type (C# or VB.NET, matching the target file's language). *If this member has attributes or XML documentation, they MUST be included here.* To Delete the target instead, set this to `// Delete {memberName}` (C#) or `' Delete {memberName}` (VB.NET).")] string newMemberCode,
        string commitMessage,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedMemberName, nameof(fullyQualifiedMemberName), logger);
            ErrorHandlingHelpers.ValidateStringParameter(newMemberCode, nameof(newMemberCode), logger);
            newMemberCode = newMemberCode.TrimBackslash();

            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(OverwriteMember));
            logger.LogInformation("Executing '{OverwriteMember}' for: {SymbolName}", nameof(OverwriteMember), fullyQualifiedMemberName);

            var symbol = await ToolHelpers.GetRoslynSymbolOrThrowAsync(solutionManager, fullyQualifiedMemberName, cancellationToken);

            if (!symbol.DeclaringSyntaxReferences.Any()) {
                throw new McpException($"Symbol '{fullyQualifiedMemberName}' has no declaring syntax references.");
            }

            var syntaxRef = symbol.DeclaringSyntaxReferences.First();
            var oldNode = await syntaxRef.GetSyntaxAsync(cancellationToken);

            if (solutionManager.CurrentSolution is null) {
                throw new McpException("Current solution is unexpectedly null after validation checks.");
            }

            var document = ToolHelpers.GetDocumentFromSyntaxNodeOrThrow(solutionManager.CurrentSolution, oldNode);

            bool isVB = document.Project.Language == LanguageNames.VisualBasic;

            // For C#, validate the node type; for VB, any SyntaxNode is valid
            if (!isVB && oldNode is not MemberDeclarationSyntax && oldNode is not TypeDeclarationSyntax) {
                throw new McpException($"Symbol '{fullyQualifiedMemberName}' does not represent a replaceable member or type.");
            }

            // Get a simple name for the symbol for the commit message
            string symbolName = symbol.Name;

            // Deletion markers: "// Delete" for C#, "' Delete" for VB.NET
            bool isDelete = newMemberCode.StartsWith("// Delete", StringComparison.OrdinalIgnoreCase)
                || (isVB && newMemberCode.TrimStart().StartsWith("' Delete", StringComparison.OrdinalIgnoreCase));
            string finalCommitMessage = (isDelete ? $"Delete {symbolName}" : $"Update {symbolName}") + ": " + commitMessage;
            if (isDelete) {
                if (isVB) {
                    // VB.NET deletion: remove the node directly using a language-agnostic editor
                    try {
                        var vbEditor = await DocumentEditor.CreateAsync(document, cancellationToken);
                        vbEditor.RemoveNode(oldNode);
                        var vbChangedDoc = vbEditor.GetChangedDocument();
                        var vbFormattedDoc = await modificationService.FormatDocumentAsync(vbChangedDoc, cancellationToken);
                        var vbNewSolution = vbFormattedDoc.Project.Solution;
                        await modificationService.ApplyChangesAsync(vbNewSolution, cancellationToken, finalCommitMessage);

                        var vbUpdatedDoc = solutionManager.CurrentSolution?.GetDocument(document.Id);
                        if (vbUpdatedDoc != null) {
                            var (vbHasErrors, vbErrorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(
                                solutionManager, vbUpdatedDoc, logger, cancellationToken);
                            if (!vbHasErrors)
                                vbErrorMessages = "<errorCheck>No compilation issues detected.</errorCheck>";
                            return $"Successfully deleted symbol {fullyQualifiedMemberName}.\n\n{vbErrorMessages}";
                        }
                        return $"Successfully deleted symbol {fullyQualifiedMemberName}";
                    } catch (Exception ex) when (ex is not McpException && ex is not OperationCanceledException) {
                        logger.LogError(ex, "Failed to delete VB.NET symbol {SymbolName}", fullyQualifiedMemberName);
                        throw new McpException($"Failed to delete symbol {fullyQualifiedMemberName}: {ex.Message}");
                    }
                }

                var commentTrivia = SyntaxFactory.Comment(newMemberCode);
                var emptyNode = SyntaxFactory.EmptyStatement()
                    .WithLeadingTrivia(commentTrivia)
                    .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));

                try {
                    var newSolution = await modificationService.ReplaceNodeAsync(document.Id, oldNode, emptyNode, cancellationToken);
                    await modificationService.ApplyChangesAsync(newSolution, cancellationToken, finalCommitMessage);

                    var updatedDocument = solutionManager.CurrentSolution.GetDocument(document.Id);
                    if (updatedDocument != null) {
                        var (hasErrors, errorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(
                            solutionManager, updatedDocument, logger, cancellationToken);
                        if (!hasErrors)
                            errorMessages = "<errorCheck>No compilation issues detected.</errorCheck>";

                        return $"Successfully deleted symbol {fullyQualifiedMemberName}.\n\n{errorMessages}";
                    }
                    return $"Successfully deleted symbol {fullyQualifiedMemberName}";
                } catch (Exception ex) when (ex is not McpException && ex is not OperationCanceledException) {
                    logger.LogError(ex, "Failed to delete symbol {SymbolName}", fullyQualifiedMemberName);
                    throw new McpException($"Failed to delete symbol {fullyQualifiedMemberName}: {ex.Message}");
                }
            }

            SyntaxNode? newNode;
            try {
                if (isVB) {
                    // VB.NET: parse with the VB parser
                    var vbTree = VisualBasicSyntaxTree.ParseText(newMemberCode);
                    var vbRoot = vbTree.GetRoot(cancellationToken) as VBSyntax.CompilationUnitSyntax;
                    newNode = vbRoot?.Members.FirstOrDefault();
                    if (newNode is null) {
                        throw new McpException("Failed to parse VB.NET code as a valid member or type declaration. The parsed result was empty.");
                    }
                } else {
                    var parsedCode = SyntaxFactory.ParseCompilationUnit(newMemberCode);
                    newNode = parsedCode.Members.FirstOrDefault();

                    if (newNode is null) {
                        throw new McpException("Failed to parse new code as a valid member or type declaration. The parsed result was empty.");
                    }

                    // Validate that the parsed node is of an expected type if the original was a TypeDeclaration
                    if (oldNode is TypeDeclarationSyntax && newNode is not TypeDeclarationSyntax) {
                        throw new McpException($"The new code for '{fullyQualifiedMemberName}' was parsed as a {newNode.Kind()}, but a TypeDeclaration was expected to replace the existing TypeDeclaration.");
                    }
                    // Validate that the parsed node is of an expected type if the original was a MemberDeclaration (but not a TypeDeclaration, which is a subtype)
                    else if (oldNode is MemberDeclarationSyntax && oldNode is not TypeDeclarationSyntax && newNode is not MemberDeclarationSyntax) {
                        throw new McpException($"The new code for '{fullyQualifiedMemberName}' was parsed as a {newNode.Kind()}, but a MemberDeclaration was expected to replace the existing MemberDeclaration.");
                    }
                }
            } catch (Exception ex) when (ex is not McpException && ex is not OperationCanceledException) {
                logger.LogError(ex, "Failed to parse replacement code for {SymbolName}", fullyQualifiedMemberName);
                throw new McpException($"Invalid syntax in replacement code: {ex.Message}");
            }

            if (newNode is null) { // Should be caught by earlier checks, but as a safeguard.
                throw new McpException("Critical error: Failed to parse new code and newNode is null.");
            }

            try {
                Solution newSolution;
                if (isVB) {
                    // For VB.NET, use a language-agnostic document editor to replace the node
                    var vbEditor = await DocumentEditor.CreateAsync(document, cancellationToken);
                    vbEditor.ReplaceNode(oldNode, newNode);
                    var vbChangedDoc = vbEditor.GetChangedDocument();
                    var vbFormattedDoc = await modificationService.FormatDocumentAsync(vbChangedDoc, cancellationToken);
                    newSolution = vbFormattedDoc.Project.Solution;
                } else {
                    newSolution = await modificationService.ReplaceNodeAsync(document.Id, oldNode, newNode, cancellationToken);
                }
                await modificationService.ApplyChangesAsync(newSolution, cancellationToken, finalCommitMessage);

                if (solutionManager.CurrentSolution is null) {
                    throw new McpException("Current solution is unexpectedly null after applying changes.");
                }

                // Generate diff using the centralized ContextInjectors
                var diffResult = ContextInjectors.CreateCodeDiff(oldNode.ToFullString(), newNode.ToFullString());

                var updatedDocument = solutionManager.CurrentSolution.GetDocument(document.Id);
                if (updatedDocument is null) {
                    logger.LogError("Updated document for {SymbolName} is null after applying changes", fullyQualifiedMemberName);
                    throw new McpException($"Failed to retrieve updated document for {fullyQualifiedMemberName} after applying changes.");
                }

                var (hasErrors, errorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(
                    solutionManager, updatedDocument, logger, cancellationToken);
                if (!hasErrors)
                    errorMessages = "<errorCheck>No compilation issues detected.</errorCheck>";

                return $"Successfully replaced symbol {fullyQualifiedMemberName}.\n\n{diffResult}\n\n{errorMessages}";


            } catch (Exception ex) when (ex is not McpException && ex is not OperationCanceledException) {
                logger.LogError(ex, "Failed to replace symbol {SymbolName}", fullyQualifiedMemberName);
                throw new McpException($"Failed to replace symbol {fullyQualifiedMemberName}: {ex.Message}");
            }
        }, logger, nameof(OverwriteMember), cancellationToken);
    }
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(RenameSymbol), Idempotent = true, Destructive = true, OpenWorld = false, ReadOnly = false),
    Description("Renames a symbol (variable, method, property, type) and updates all references. Changes are formatted.")]
    public static async Task<string> RenameSymbol(
        ISolutionManager solutionManager,
        ICodeModificationService modificationService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("FQN of the symbol to rename.")] string fullyQualifiedSymbolName,
        [Description("The new name for the symbol.")] string newName,
        string commitMessage,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedSymbolName, "fullyQualifiedSymbolName", logger);
            ErrorHandlingHelpers.ValidateStringParameter(newName, "newName", logger);

            // Ensure solution is loaded first so we can find the symbol and check its language
            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(RenameSymbol));
            logger.LogInformation("Executing '{RenameSymbol}' for {SymbolName} to {NewName}", nameof(RenameSymbol), fullyQualifiedSymbolName, newName);

            // Get the symbol to rename
            var symbol = await ToolHelpers.GetRoslynSymbolOrThrowAsync(solutionManager, fullyQualifiedSymbolName, cancellationToken);

            // Validate the new name is a valid identifier for the symbol's language
            if (!IsValidIdentifier(newName, symbol.Language)) {
                throw new McpException($"'{newName}' is not a valid identifier for {symbol.Language} renaming.");
            }

            // Check if symbol is renamable
            if (symbol.IsImplicitlyDeclared) {
                throw new McpException($"Cannot rename implicitly declared symbol '{fullyQualifiedSymbolName}'.");
            }

            string finalCommitMessage = $"Rename {symbol.Name} to {newName}: " + commitMessage;

            try {
                // Perform the rename operation
                var newSolution = await modificationService.RenameSymbolAsync(symbol, newName, cancellationToken);

                // Check if the operation actually made changes
                var changeset = newSolution.GetChanges(solutionManager.CurrentSolution!);
                var changedDocumentCount = changeset.GetProjectChanges().Sum(p => p.GetChangedDocuments().Count());

                if (changedDocumentCount == 0) {
                    logger.LogWarning("Rename operation for {SymbolName} to {NewName} produced no changes",
                        fullyQualifiedSymbolName, newName);
                }

                // Apply the changes with the commit message
                await modificationService.ApplyChangesAsync(newSolution, cancellationToken, finalCommitMessage);

                // Check for compilation errors after renaming the symbol using centralized ContextInjectors
                // Get the first few affected documents to check
                var affectedDocumentIds = changeset.GetProjectChanges()
                    .SelectMany(pc => pc.GetChangedDocuments())
                    .Take(5)  // Limit to first 5 documents to avoid excessive checking
                    .ToList();

                StringBuilder errorBuilder = new StringBuilder("<errorCheck>");

                // Check each affected document for compilation errors
                foreach (var docId in affectedDocumentIds) {
                    if (solutionManager.CurrentSolution != null) {
                        var updatedDoc = solutionManager.CurrentSolution.GetDocument(docId);
                        if (updatedDoc != null) {
                            var (docHasErrors, docErrorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(
                                solutionManager, updatedDoc, logger, cancellationToken);

                            if (docHasErrors) {
                                errorBuilder.AppendLine($"Issues in file {updatedDoc.FilePath ?? "unknown"}:");
                                errorBuilder.AppendLine(docErrorMessages);
                                errorBuilder.AppendLine();
                            } else {
                                errorBuilder.AppendLine($"No compilation issues in file {updatedDoc.FilePath ?? "unknown"}.");
                            }
                        }
                    }
                }
                errorBuilder.AppendLine("</errorCheck>");

                return $"Symbol '{symbol.Name}' (originally '{fullyQualifiedSymbolName}') successfully renamed to '{newName}' and references updated in {changedDocumentCount} documents.\n\n{errorBuilder}";

            } catch (InvalidOperationException ex) {
                logger.LogError(ex, "Invalid rename operation for {SymbolName} to {NewName}", fullyQualifiedSymbolName, newName);
                throw new McpException($"Cannot rename symbol '{fullyQualifiedSymbolName}' to '{newName}': {ex.Message}");
            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Failed to rename symbol {SymbolName} to {NewName}", fullyQualifiedSymbolName, newName);
                throw new McpException($"Failed to rename symbol '{fullyQualifiedSymbolName}' to '{newName}': {ex.Message}");
            }
        }, logger, nameof(RenameSymbol), cancellationToken);
    }
    // Helper method to check if a string is a valid identifier for the specified language
    private static bool IsValidIdentifier(string name, string language = "") {
        if (language == LanguageNames.VisualBasic) {
            return Microsoft.CodeAnalysis.VisualBasic.SyntaxFacts.IsValidIdentifier(name);
        }
        // Default to C# validation (also used as a general fallback)
        return SyntaxFacts.IsValidIdentifier(name);
    }

    //Disabled for now
    //[McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(ReplaceAllReferences), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("Surgically replaces all references to a symbol with new code across the solution. Perfect for systematic API upgrades - e.g., replacing all Console.WriteLine() calls with Logger.Info(). Use filename filters (*.cs, *.vb, Controller*.cs) to scope changes to specific files.")]
    public static async Task<string> ReplaceAllReferences(
        ISolutionManager solutionManager,
        ICodeModificationService modificationService,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("FQN of the symbol whose references should be replaced.")] string fullyQualifiedSymbolName,
        [Description("The code to replace references with (C# or VB.NET, matching the target file's language).")] string replacementCode,
        [Description("Only replace symbols in files with this pattern. Supports globbing (`*`).")] string filenameFilter,
        string commitMessage,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedSymbolName, "fullyQualifiedSymbolName", logger);
            ErrorHandlingHelpers.ValidateStringParameter(replacementCode, "replacementCode", logger);
            replacementCode = replacementCode.TrimBackslash();

            // Note: filenameFilter can be empty or null, as this indicates "replace in all files"

            // Ensure solution is loaded
            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(ReplaceAllReferences));
            logger.LogInformation("Executing '{ReplaceAllReferences}' for {SymbolName} with text '{ReplacementCode}', filter: {Filter}",
                nameof(ReplaceAllReferences), fullyQualifiedSymbolName, replacementCode, filenameFilter ?? "none");

            // Get the symbol whose references will be replaced
            var symbol = await ToolHelpers.GetRoslynSymbolOrThrowAsync(solutionManager, fullyQualifiedSymbolName, cancellationToken);

            // Create a shortened version of the replacement code for the commit message
            string shortReplacementCode = replacementCode.Length > 30
                ? replacementCode.Substring(0, 30) + "..."
                : replacementCode;

            string finalCommitMessage = $"Replace references to {symbol.Name} with {shortReplacementCode}: " + commitMessage;

            // Validate that the replacement code can be parsed as a valid C# expression
            try {
                var expressionSyntax = SyntaxFactory.ParseExpression(replacementCode);
                if (expressionSyntax == null) {
                    logger.LogWarning("Replacement code '{ReplacementCode}' may not be a valid C# expression", replacementCode);
                }
            } catch (Exception ex) {
                logger.LogWarning(ex, "Replacement code '{ReplacementCode}' could not be parsed as a C# expression", replacementCode);
                // We don't throw here, because some valid replacements might not be valid expressions on their own
            }

            // Create a predicate filter if a filename filter is provided
            Func<SyntaxNode, bool>? predicateFilter = null;
            if (!string.IsNullOrEmpty(filenameFilter)) {
                Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
                string normalizedFilter = filenameFilter.Replace('\\', '/');
                matcher.AddInclude(normalizedFilter);

                string root = Path.GetPathRoot(solutionManager.CurrentSolution?.FilePath) ?? Path.GetPathRoot(Environment.CurrentDirectory)!;
                try {
                    predicateFilter = node => {
                        try {
                            var location = node.GetLocation();
                            if (location == null || string.IsNullOrWhiteSpace(location.SourceTree?.FilePath)) {
                                return false;
                            }
                            string filePath = location.SourceTree.FilePath;
                            return matcher.Match(root, filePath).HasMatches;
                        } catch (Exception ex) {
                            logger.LogWarning(ex, "Error applying filename filter to node");
                            return false; // Skip nodes that cause errors in the filter
                        }
                    };
                } catch (Exception ex) {
                    logger.LogError(ex, "Failed to create filename filter '{Filter}'", filenameFilter);
                    throw new McpException($"Failed to create filename filter '{filenameFilter}': {ex.Message}");
                }
            }

            try {
                // Replace all references to the symbol with the new code
                var newSolution = await modificationService.ReplaceAllReferencesAsync(
                    symbol, replacementCode, cancellationToken, predicateFilter);

                // Count changes before applying them
                if (solutionManager.CurrentSolution == null) {
                    throw new McpException("Current solution is null after replacement operation.");
                }

                var originalSolution = solutionManager.CurrentSolution;
                var solutionChanges = newSolution.GetChanges(originalSolution);
                var changedDocumentsCount = solutionChanges.GetProjectChanges()
                    .SelectMany(pc => pc.GetChangedDocuments())
                    .Count();

                if (changedDocumentsCount == 0) {
                    logger.LogWarning("No documents were changed when replacing references to '{SymbolName}'",
                        fullyQualifiedSymbolName);

                    // We can't directly check for references without applying changes
                    // Just give a general message about no changes being made

                    if (!string.IsNullOrEmpty(filenameFilter)) {
                        // If the filter is limiting results
                        return $"No references to '{symbol.Name}' found in files matching '{filenameFilter}'. No changes were made.";
                    } else {
                        // General message about no changes
                        return $"References to '{symbol.Name}' were found but no changes were made. The replacement code might be identical to the original.";
                    }
                }

                // Apply the changes
                await modificationService.ApplyChangesAsync(newSolution, cancellationToken, finalCommitMessage);

                // Check for compilation errors in changed documents
                var changedDocIds = solutionChanges.GetProjectChanges()
                    .SelectMany(pc => pc.GetChangedDocuments())
                    .Take(5) // Limit to first 5 documents to avoid excessive checking
                    .ToList();

                StringBuilder errorBuilder = new StringBuilder("<errorCheck>");

                // Check each affected document for compilation errors
                foreach (var docId in changedDocIds) {
                    if (solutionManager.CurrentSolution != null) {
                        var updatedDoc = solutionManager.CurrentSolution.GetDocument(docId);
                        if (updatedDoc != null) {
                            var (docHasErrors, docErrorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(
                solutionManager, updatedDoc, logger, cancellationToken);

                            if (docHasErrors) {
                                errorBuilder.AppendLine($"Issues in file {updatedDoc.FilePath ?? "unknown"}:");
                                errorBuilder.AppendLine(docErrorMessages);
                                errorBuilder.AppendLine();
                            } else {
                                errorBuilder.AppendLine($"No compilation issues in file {updatedDoc.FilePath ?? "unknown"}.");
                            }
                        }
                    }
                }
                errorBuilder.AppendLine("</errorCheck>");

                var filterMessage = string.IsNullOrEmpty(filenameFilter) ? "" : $" (with filter '{filenameFilter}')";

                return $"Successfully replaced references to '{symbol.Name}'{filterMessage} with '{replacementCode}' in {changedDocumentsCount} document(s).\n\n{errorBuilder}";

            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Failed to replace references to symbol '{SymbolName}' with '{ReplacementCode}'",
                    fullyQualifiedSymbolName, replacementCode);
                throw new McpException($"Failed to replace references: {ex.Message}");
            }
        }, logger, nameof(ReplaceAllReferences), cancellationToken);
    }
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(Undo), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description($"Reverts the last applied change to the solution. You can undo all consecutive changes you have made. Returns a diff of the change that was undone.")]
    public static async Task<string> Undo(
        ISolutionManager solutionManager,
        ICodeModificationService modificationService,
        ILogger<ModificationToolsLogCategory> logger,
        CancellationToken cancellationToken) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(Undo));
            logger.LogInformation("Executing '{UndoLastChange}'", nameof(Undo));

            var (success, message) = await modificationService.UndoLastChangeAsync(cancellationToken);
            if (!success) {
                logger.LogWarning("Undo operation failed: {Message}", message);
                throw new McpException($"Failed to undo the last change. {message}");
            }
            logger.LogInformation("Undo operation succeeded");
            return message;

        }, logger, nameof(Undo), cancellationToken);
    }
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(FindAndReplace), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("Every developer's favorite. Use this for all small edits (code tweaks, usings, namespaces, interface implementations, attributes, etc.) instead of rewriting large members or types.")]
    public static async Task<string> FindAndReplace(
        ISolutionManager solutionManager,
        ICodeModificationService modificationService,
        IDocumentOperationsService documentOperations,
        ILogger<ModificationToolsLogCategory> logger,
        [Description("Regex operating in multiline mode, so `^` and `$` match per line. Always use `\\s*` at the beginnings of lines for unknown indentation. Make sure to escape your escapes for json.")] string regexPattern,
        [Description("Replacement text, which can include regex groups ($1, ${name}, etc.)")] string replacementText,
        [Description("Target, which can be either a FQN (replaces text within a declaration) or a filepath supporting globbing (`*`) (replaces all instances across files)")] string target,
        string commitMessage,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            // Validate parameters
            ErrorHandlingHelpers.ValidateStringParameter(regexPattern, "regexPattern", logger);
            ErrorHandlingHelpers.ValidateStringParameter(target, "targetString", logger);

            //normalize newlines in pattern
            regexPattern = regexPattern
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", @"\n")
            .Replace(@"\r\n", @"\n")
            .Replace(@"\r", @"\n");

            // Ensure solution is loaded
            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(FindAndReplace));
            logger.LogInformation("Executing '{FindAndReplace}' with pattern: '{Pattern}', replacement: {Replacement}, target: {Target}",
                nameof(FindAndReplace), regexPattern, replacementText, target);

            // Validate the regex pattern
            try {
                // Create the regex with multiline option to test it
                _ = new Regex(regexPattern, RegexOptions.Multiline);
            } catch (ArgumentException ex) {
                throw new McpException($"Invalid regular expression pattern: {ex.Message}");
            }

            try {
                // Get the original solution for later comparison
                var originalSolution = solutionManager.CurrentSolution ?? throw new McpException("Current solution is null before find and replace operation.");

                // Track all modified files for both code and non-code files
                var modifiedFiles = new List<string>();
                var changedDocuments = new List<DocumentId>();
                var nonCodeFilesModified = new List<string>();
                var nonCodeDiffs = new Dictionary<string, string>();

                // First, check if the target is a file path pattern (contains wildcard or is a direct file path)
                if (target.Contains("*") || target.Contains("?") || (File.Exists(target) && !documentOperations.IsCodeFile(target))) {
                    logger.LogInformation("Target appears to be a file path pattern or non-code file: {Target}", target);

                    // Normalize the target path to use forward slashes consistently
                    string normalizedTarget = target.Replace('\\', '/');

                    // Create matcher for the pattern
                    Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
                    matcher.AddInclude(normalizedTarget);

                    string root = Path.GetPathRoot(originalSolution.FilePath) ?? Path.GetPathRoot(Environment.CurrentDirectory)!;

                    // Get all files in solution directory matching the pattern
                    var solutionDirectory = Path.GetDirectoryName(originalSolution.FilePath);
                    if (string.IsNullOrEmpty(solutionDirectory)) {
                        throw new McpException("Could not determine solution directory");
                    }

                    // Handle direct file path (no wildcards)
                    if (!target.Contains("*") && !target.Contains("?") && File.Exists(target)) {
                        // Direct file path, process just this file
                        var pathInfo = documentOperations.GetPathInfo(target);

                        if (!pathInfo.IsWithinSolutionDirectory) {
                            throw new McpException($"File {target} exists but is outside the solution directory. Cannot modify for safety reasons.");
                        }

                        // Check if it's a non-code file
                        if (!documentOperations.IsCodeFile(target)) {
                            var (changed, diff) = await ProcessNonCodeFile(target, regexPattern, replacementText, documentOperations, nonCodeFilesModified, cancellationToken);
                            if (changed) {
                                nonCodeDiffs.Add(target, diff);
                            }
                        }
                    } else {
                        // Use glob pattern to find matching files
                        DirectoryInfo dirInfo = new DirectoryInfo(solutionDirectory);
                        List<FileInfo> allFiles = dirInfo.GetFiles("*.*", SearchOption.AllDirectories).ToList();
                        string rootDir = Path.GetPathRoot(solutionDirectory) ?? Path.GetPathRoot(Environment.CurrentDirectory)!;
                        foreach (var file in allFiles) {
                            if (matcher.Match(rootDir, file.FullName).HasMatches) {
                                var pathInfo = documentOperations.GetPathInfo(file.FullName);

                                // Skip files in unsafe directories or outside solution
                                if (!pathInfo.IsWithinSolutionDirectory || !string.IsNullOrEmpty(pathInfo.WriteRestrictionReason)) {
                                    logger.LogWarning("Skipping file due to restrictions: {FilePath}, Reason: {Reason}",
                                        file.FullName, pathInfo.WriteRestrictionReason ?? "Outside solution directory");
                                    continue;
                                }

                                // Process non-code files directly
                                if (!documentOperations.IsCodeFile(file.FullName)) {
                                    var (changed, diff) = await ProcessNonCodeFile(file.FullName, regexPattern, replacementText, documentOperations, nonCodeFilesModified, cancellationToken);
                                    if (changed) {
                                        nonCodeDiffs.Add(file.FullName, diff);
                                    }
                                }
                            }
                        }
                    }
                }

                // Now process code files through the Roslyn workspace
                var newSolution = await modificationService.FindAndReplaceAsync(
                    target, regexPattern, replacementText, cancellationToken, RegexOptions.Multiline);

                // Get changed code documents
                var solutionChanges = newSolution.GetChanges(originalSolution);
                foreach (var projectChange in solutionChanges.GetProjectChanges()) {
                    changedDocuments.AddRange(projectChange.GetChangedDocuments());
                }

                // If no code or non-code files were modified, return early
                if (changedDocuments.Count == 0 && nonCodeFilesModified.Count == 0) {
                    logger.LogWarning("No documents were changed during find and replace operation");
                    throw new McpException($"No matches found for pattern '{regexPattern}' in target '{target}', or matches were found but replacement produced identical text. No changes were made.");
                }

                // Add code document file paths to the modifiedFiles list
                foreach (var docId in changedDocuments) {
                    var document = originalSolution.GetDocument(docId);
                    if (document?.FilePath != null) {
                        modifiedFiles.Add(document.FilePath);
                    }
                }

                // Add non-code files to the list
                modifiedFiles.AddRange(nonCodeFilesModified);

                string finalCommitMessage = $"Find and replace on {target}: {commitMessage}";

                // Apply the changes to code files
                if (changedDocuments.Count > 0) {
                    await modificationService.ApplyChangesAsync(newSolution, cancellationToken, finalCommitMessage, nonCodeFilesModified);
                }

                // Commit non-code files (if we only modified non-code files)
                if (nonCodeFilesModified.Count > 0 && changedDocuments.Count == 0) {
                    // Get solution path
                    var solutionPath = originalSolution.FilePath;
                    if (string.IsNullOrEmpty(solutionPath)) {
                        logger.LogDebug("Solution path is not available, skipping Git operations for non-code files");
                    } else {
                        await documentOperations.ProcessGitOperationsAsync(nonCodeFilesModified, cancellationToken, finalCommitMessage);
                    }
                }

                // Check for compilation errors in changed code documents
                var changedDocIds = changedDocuments.Take(5).ToList(); // Limit to first 5 documents
                StringBuilder errorBuilder = new StringBuilder("<errorCheck>");

                // Check each affected document for compilation errors
                foreach (var docId in changedDocIds) {
                    if (solutionManager.CurrentSolution != null) {
                        var updatedDoc = solutionManager.CurrentSolution.GetDocument(docId);
                        if (updatedDoc != null) {
                            var (docHasErrors, docErrorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(
                                solutionManager, updatedDoc, logger, cancellationToken);

                            if (docHasErrors) {
                                errorBuilder.AppendLine($"Issues in file {updatedDoc.FilePath ?? "unknown"}:");
                                errorBuilder.AppendLine(docErrorMessages);
                                errorBuilder.AppendLine();
                            } else {
                                errorBuilder.AppendLine($"No compilation issues in file {updatedDoc.FilePath ?? "unknown"}.");
                            }
                        }
                    }
                }
                errorBuilder.AppendLine("</errorCheck>");

                // Generate multi-document diff for code files
                string diffOutput = changedDocuments.Count > 0
                    ? await ContextInjectors.CreateMultiDocumentDiff(
                        originalSolution,
                        newSolution,
                        changedDocuments,
                        5,
                        cancellationToken)
                    : "";

                // For non-code files, build a similar diff output format
                StringBuilder nonCodeDiffBuilder = new StringBuilder();
                if (nonCodeDiffs.Count > 0) {
                    nonCodeDiffBuilder.AppendLine();
                    nonCodeDiffBuilder.AppendLine("Non-code file changes:");

                    int nonCodeFileCount = 0;
                    foreach (var diffEntry in nonCodeDiffs) {
                        if (nonCodeFileCount >= 5) {
                            nonCodeDiffBuilder.AppendLine($"...and {nonCodeDiffs.Count - 5} more non-code files");
                            break;
                        }
                        nonCodeDiffBuilder.AppendLine();
                        nonCodeDiffBuilder.AppendLine($"Document: {diffEntry.Key}");
                        nonCodeDiffBuilder.AppendLine(diffEntry.Value);
                        nonCodeFileCount++;
                    }
                }

                return $"Successfully replaced pattern '{regexPattern}' with '{replacementText}' in {modifiedFiles.Count} file(s).\n\n{errorBuilder}\n\n{diffOutput}{nonCodeDiffBuilder}";

            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Failed to replace pattern '{Pattern}' with '{Replacement}' in '{Target}'",
                    regexPattern, replacementText, target);
                throw new McpException($"Failed to perform find and replace: {ex.Message}");
            }
        }, logger, nameof(FindAndReplace), cancellationToken);
    }
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(MoveMember), Idempotent = false, Destructive = true, OpenWorld = false, ReadOnly = false)]
    [Description("Moves a member (property, field, method, nested type, etc.) from one type/namespace to another. The member is removed from the source location and added to the destination.")]
    public static async Task<string> MoveMember(
                        ISolutionManager solutionManager,
                        ICodeModificationService modificationService,
                        ILogger<ModificationToolsLogCategory> logger,
                        [Description("FQN of the member to move.")] string fullyQualifiedMemberName,
                        [Description("FQN of the destination type or namespace where the member should be moved.")] string fullyQualifiedDestinationTypeOrNamespaceName,
                        string commitMessage,
                        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedMemberName, nameof(fullyQualifiedMemberName), logger);
            ErrorHandlingHelpers.ValidateStringParameter(fullyQualifiedDestinationTypeOrNamespaceName, nameof(fullyQualifiedDestinationTypeOrNamespaceName), logger);

            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(MoveMember));
            logger.LogInformation("Executing '{MoveMember}' moving {MemberName} to {DestinationName}",
                nameof(MoveMember), fullyQualifiedMemberName, fullyQualifiedDestinationTypeOrNamespaceName);

            if (solutionManager.CurrentSolution == null) {
                throw new McpException("Current solution is unexpectedly null after validation checks.");
            }
            Solution currentSolution = solutionManager.CurrentSolution;

            var sourceMemberSymbol = await ToolHelpers.GetRoslynSymbolOrThrowAsync(solutionManager, fullyQualifiedMemberName, cancellationToken);

            if (sourceMemberSymbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol or INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate })) {
                throw new McpException($"Symbol '{fullyQualifiedMemberName}' is not a movable member type. Only fields, properties, methods, events, and nested types can be moved.");
            }

            var destinationSymbol = await ToolHelpers.GetRoslynSymbolOrThrowAsync(solutionManager, fullyQualifiedDestinationTypeOrNamespaceName, cancellationToken);

            if (destinationSymbol is not (INamedTypeSymbol or INamespaceSymbol)) {
                throw new McpException($"Destination '{fullyQualifiedDestinationTypeOrNamespaceName}' must be a type or namespace.");
            }

            var sourceSyntaxRef = sourceMemberSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (sourceSyntaxRef == null) {
                throw new McpException($"Could not find syntax reference for member '{fullyQualifiedMemberName}'.");
            }

            var sourceMemberNode = await sourceSyntaxRef.GetSyntaxAsync(cancellationToken);
            Document sourceDocument = ToolHelpers.GetDocumentFromSyntaxNodeOrThrow(currentSolution, sourceMemberNode);
            bool isVBProject = sourceDocument.Project.Language == LanguageNames.VisualBasic;

            // Get the member declaration – C# uses MemberDeclarationSyntax, VB uses SyntaxNode
            MemberDeclarationSyntax? memberDeclaration = null;
            if (!isVBProject) {
                if (sourceMemberNode is not MemberDeclarationSyntax csDecl) {
                    throw new McpException($"Source member '{fullyQualifiedMemberName}' is not a valid member declaration.");
                }
                memberDeclaration = csDecl;
            }
            // For VB.NET, sourceMemberNode is used directly as a SyntaxNode
            INamedTypeSymbol? destinationTypeSymbol = null;
            INamespaceSymbol? destinationNamespaceSymbol = null;
            Document destinationDocument;

            if (destinationSymbol is INamedTypeSymbol typeSym) {
                destinationTypeSymbol = typeSym;
                var destSyntaxRef = typeSym.DeclaringSyntaxReferences.FirstOrDefault();
                if (destSyntaxRef == null) {
                    throw new McpException($"Could not find syntax reference for destination type '{fullyQualifiedDestinationTypeOrNamespaceName}'.");
                }
                var destNode = await destSyntaxRef.GetSyntaxAsync(cancellationToken);
                destinationDocument = ToolHelpers.GetDocumentFromSyntaxNodeOrThrow(currentSolution, destNode);
            } else if (destinationSymbol is INamespaceSymbol nsSym) {
                destinationNamespaceSymbol = nsSym;
                var projectForDestination = currentSolution.GetDocument(sourceDocument.Id)!.Project;
                var existingDoc = await FindExistingDocumentWithNamespaceAsync(projectForDestination, nsSym, cancellationToken);
                if (existingDoc != null) {
                    destinationDocument = existingDoc;
                } else {
                    var newDoc = await CreateDocumentForNamespaceAsync(projectForDestination, nsSym, cancellationToken);
                    destinationDocument = newDoc;
                    currentSolution = newDoc.Project.Solution; // Update currentSolution after adding a document
                }
            } else {
                throw new McpException("Invalid destination symbol type.");
            }

            if (sourceDocument.Id == destinationDocument.Id && sourceMemberSymbol.ContainingSymbol.Equals(destinationSymbol, SymbolEqualityComparer.Default)) {
                throw new McpException($"Source and destination are the same. Member '{fullyQualifiedMemberName}' is already in '{fullyQualifiedDestinationTypeOrNamespaceName}'.");
            }

            string memberName;
            if (isVBProject) {
                // For VB.NET, extract the name from the symbol directly
                memberName = sourceMemberSymbol.Name;
            } else {
                memberName = GetMemberName(memberDeclaration!);
            }
            INamedTypeSymbol? updatedDestinationTypeSymbol = null;
            if (destinationTypeSymbol != null) {
                // Re-resolve destinationTypeSymbol from the potentially updated currentSolution
                var destinationDocumentFromCurrentSolution = currentSolution.GetDocument(destinationDocument.Id)
                    ?? throw new McpException($"Destination document '{destinationDocument.FilePath}' not found in current solution for symbol re-resolution.");
                var tempDestSymbol = await SymbolFinder.FindSymbolAtPositionAsync(destinationDocumentFromCurrentSolution, destinationTypeSymbol.Locations.First().SourceSpan.Start, cancellationToken);
                updatedDestinationTypeSymbol = tempDestSymbol as INamedTypeSymbol;
                if (updatedDestinationTypeSymbol == null) {
                    throw new McpException($"Could not re-resolve destination type symbol '{destinationTypeSymbol.ToDisplayString()}' in the current solution state at file '{destinationDocumentFromCurrentSolution.FilePath}'. Original location span: {destinationTypeSymbol.Locations.First().SourceSpan}");
                }
            }

            if (!isVBProject && updatedDestinationTypeSymbol != null && !IsMemberAllowed(updatedDestinationTypeSymbol, memberDeclaration!, memberName, cancellationToken)) {
                throw new McpException($"A member with the name '{memberName}' already exists in destination type '{fullyQualifiedDestinationTypeOrNamespaceName}'.");
            }

            try {
                var actualDestinationDocument = currentSolution.GetDocument(destinationDocument.Id)
                    ?? throw new McpException($"Destination document '{destinationDocument.FilePath}' not found in current solution before adding member.");

                if (isVBProject) {
                    // VB.NET: use language-agnostic DocumentEditor for both add and remove
                    if (updatedDestinationTypeSymbol != null) {
                        // Find the destination type's syntax node
                        var destSyntaxRef = updatedDestinationTypeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                        if (destSyntaxRef == null)
                            throw new McpException($"Could not find syntax reference for destination type '{fullyQualifiedDestinationTypeOrNamespaceName}'.");
                        var destTypeNode = await destSyntaxRef.GetSyntaxAsync(cancellationToken);
                        var destEditor = await DocumentEditor.CreateAsync(actualDestinationDocument, cancellationToken);
                        destEditor.AddMember(destTypeNode, sourceMemberNode);
                        var destChanged = destEditor.GetChangedDocument();
                        var destFormatted = await modificationService.FormatDocumentAsync(destChanged, cancellationToken);
                        currentSolution = destFormatted.Project.Solution;
                    } else {
                        if (destinationNamespaceSymbol == null) throw new McpException("Destination namespace symbol is null when expected for namespace move.");
                        // Add to namespace using VB-specific approach
                        currentSolution = await AddMemberToNamespaceVBAsync(actualDestinationDocument, destinationNamespaceSymbol, sourceMemberNode, modificationService, cancellationToken);
                    }

                    // Remove from source using language-agnostic RemoveNode
                    var sourceDocInSolution = currentSolution.GetDocument(sourceDocument.Id)
                        ?? throw new McpException("Source document not found in current solution after adding member to destination.");
                    var sourceRoot = await sourceDocInSolution.GetSyntaxRootAsync(cancellationToken)
                        ?? throw new McpException("Could not get syntax root for source document.");
                    var sourceNodeToRemove = sourceRoot.FindNode(sourceMemberNode.Span, findInsideTrivia: true, getInnermostNodeForTie: true)
                        ?? sourceMemberNode;
                    var removeEditor = await DocumentEditor.CreateAsync(sourceDocInSolution, cancellationToken);
                    removeEditor.RemoveNode(sourceNodeToRemove);
                    var removeChanged = removeEditor.GetChangedDocument();
                    var removeFormatted = await modificationService.FormatDocumentAsync(removeChanged, cancellationToken);
                    currentSolution = removeFormatted.Project.Solution;
                } else {
                    if (updatedDestinationTypeSymbol != null) {
                        currentSolution = await modificationService.AddMemberAsync(actualDestinationDocument.Id, updatedDestinationTypeSymbol, memberDeclaration!, -1, cancellationToken);
                    } else {
                        if (destinationNamespaceSymbol == null) throw new McpException("Destination namespace symbol is null when expected for namespace move.");
                        currentSolution = await AddMemberToNamespaceAsync(actualDestinationDocument, destinationNamespaceSymbol, memberDeclaration!, modificationService, cancellationToken);
                    }

                    // Re-acquire source document and node from the *new* currentSolution
                    var sourceDocumentInCurrentSolution = currentSolution.GetDocument(sourceDocument.Id)
                        ?? throw new McpException("Source document not found in current solution after adding member to destination.");
                    var syntaxRootOfSourceInCurrentSolution = await sourceDocumentInCurrentSolution.GetSyntaxRootAsync(cancellationToken)
                        ?? throw new McpException("Could not get syntax root for source document in current solution.");

                    // Attempt to find the node again. Its span might have changed if the destination was in the same file.
                    var sourceMemberNodeInCurrentTree = syntaxRootOfSourceInCurrentSolution.FindNode(sourceMemberNode.Span, findInsideTrivia: true, getInnermostNodeForTie: true);
                    if (sourceMemberNodeInCurrentTree == null || !(sourceMemberNodeInCurrentTree is MemberDeclarationSyntax)) {
                        // Fallback: Try to find by kind and name if span-based lookup failed (e.g. due to formatting changes or other modifications)
                        sourceMemberNodeInCurrentTree = syntaxRootOfSourceInCurrentSolution
                            .DescendantNodes()
                            .OfType<MemberDeclarationSyntax>()
                            .FirstOrDefault(m => m.Kind() == memberDeclaration!.Kind() && GetMemberName(m) == memberName);

                        if (sourceMemberNodeInCurrentTree == null) {
                            logger.LogWarning("Could not precisely re-locate source member node by original span or by kind/name after destination add. Original span: {Span}. Member kind: {Kind}, Name: {Name}. File: {File}", sourceMemberNode.Span, memberDeclaration!.Kind(), memberName, sourceDocumentInCurrentSolution.FilePath);
                            // As a last resort, if the original node is still part of the new tree (by reference), use it.
                            // This is risky if the tree has been significantly changed, but better than failing if it's just minor formatting.
                            if (syntaxRootOfSourceInCurrentSolution.DescendantNodes().Contains(sourceMemberNode)) {
                                sourceMemberNodeInCurrentTree = sourceMemberNode;
                                logger.LogWarning("Fallback: Using original source member node reference for removal. This might be risky if tree changed significantly.");
                            } else {
                                throw new McpException($"Critically failed to re-locate source member node '{memberName}' in '{sourceDocumentInCurrentSolution.FilePath}' for removal after modifications. Original span {sourceMemberNode.Span}. This usually indicates significant tree changes that broke span tracking or the member was unexpectedly altered or removed.");
                            }
                        } else {
                            logger.LogInformation("Re-located source member node by kind and name for removal. Original span: {OriginalSpan}, New span: {NewSpan}", sourceMemberNode.Span, sourceMemberNodeInCurrentTree.Span);
                        }
                    }

                    currentSolution = await RemoveMemberFromParentAsync(sourceDocumentInCurrentSolution, sourceMemberNodeInCurrentTree, modificationService, cancellationToken);
                }

                string finalCommitMessage = $"Move {memberName} to {fullyQualifiedDestinationTypeOrNamespaceName}: {commitMessage}";
                await modificationService.ApplyChangesAsync(currentSolution, cancellationToken, finalCommitMessage);

                // After ApplyChangesAsync, the solutionManager.CurrentSolution should be the most up-to-date.
                // Re-fetch documents from there for final error checking.
                var finalSourceDocumentAfterApply = solutionManager.CurrentSolution?.GetDocument(sourceDocument.Id);
                var finalDestinationDocumentAfterApply = solutionManager.CurrentSolution?.GetDocument(destinationDocument.Id);

                StringBuilder errorBuilder = new StringBuilder("<errorCheck>");
                if (finalSourceDocumentAfterApply != null) {
                    var (sourceHasErrors, sourceErrorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(solutionManager, finalSourceDocumentAfterApply, logger, cancellationToken);
                    errorBuilder.AppendLine(sourceHasErrors
                        ? $"Issues in source file {finalSourceDocumentAfterApply.FilePath ?? "unknown"}:\n{sourceErrorMessages}\n"
                        : $"No compilation errors detected in source file {finalSourceDocumentAfterApply.FilePath ?? "unknown"}.");
                }

                if (finalDestinationDocumentAfterApply != null && (!finalSourceDocumentAfterApply?.Id.Equals(finalDestinationDocumentAfterApply.Id) ?? true)) {
                    var (destHasErrors, destErrorMessages) = await ContextInjectors.CheckCompilationErrorsAsync(solutionManager, finalDestinationDocumentAfterApply, logger, cancellationToken);
                    errorBuilder.AppendLine(destHasErrors
                        ? $"Issues in destination file {finalDestinationDocumentAfterApply.FilePath ?? "unknown"}:\n{destErrorMessages}\n"
                        : $"No compilation errors detected in destination file {finalDestinationDocumentAfterApply.FilePath ?? "unknown"}.");
                }
                errorBuilder.AppendLine("</errorCheck>");

                var sourceFilePathDisplay = finalSourceDocumentAfterApply?.FilePath ?? sourceDocument.FilePath ?? "unknown source file";
                var destinationFilePathDisplay = finalDestinationDocumentAfterApply?.FilePath ?? destinationDocument.FilePath ?? "unknown destination file";

                var locationInfo = sourceFilePathDisplay == destinationFilePathDisplay
                    ? $"within {sourceFilePathDisplay}"
                    : $"from {sourceFilePathDisplay} to {destinationFilePathDisplay}";

                return $"Successfully moved member '{memberName}' to '{fullyQualifiedDestinationTypeOrNamespaceName}' {locationInfo}.\n\n{errorBuilder}";
            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Failed to move member {MemberName} to {DestinationName}", fullyQualifiedMemberName, fullyQualifiedDestinationTypeOrNamespaceName);
                throw new McpException($"Failed to move member '{fullyQualifiedMemberName}' to '{fullyQualifiedDestinationTypeOrNamespaceName}': {ex.Message}", ex);
            }
        }, logger, nameof(MoveMember), cancellationToken);
    }
    /// <summary>
    /// Finds an existing document in the project that contains the specified namespace.
    /// Supports both C# (.cs) and VB.NET (.vb) files.
    /// </summary>
    private static async Task<Document?> FindExistingDocumentWithNamespaceAsync(Project project, INamespaceSymbol namespaceSymbol, CancellationToken cancellationToken) {
        var namespaceName = namespaceSymbol.ToDisplayString();
        bool isVB = project.Language == LanguageNames.VisualBasic;

        foreach (var document in project.Documents) {
            var ext = Path.GetExtension(document.FilePath);
            if (isVB) {
                if (!string.Equals(ext, ".vb", StringComparison.OrdinalIgnoreCase)) continue;
            } else {
                if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase)) continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken);

            if (isVB) {
                if (root is VBSyntax.CompilationUnitSyntax vbCompilationUnit) {
                    var hasNamespace = vbCompilationUnit.Members
                        .OfType<VBSyntax.NamespaceBlockSyntax>()
                        .Any(n => n.NamespaceStatement.Name.ToString() == namespaceName);

                    if (hasNamespace || (namespaceSymbol.IsGlobalNamespace && vbCompilationUnit.Members.Any())) {
                        return document;
                    }
                }
            } else {
                if (root is CompilationUnitSyntax compilationUnit) {
                    var hasNamespace = compilationUnit.Members
                        .OfType<NamespaceDeclarationSyntax>()
                        .Any(n => n.Name.ToString() == namespaceName);

                    if (hasNamespace || (namespaceSymbol.IsGlobalNamespace && compilationUnit.Members.Any())) {
                        return document;
                    }
                }
            }
        }

        return null;
    }
    /// <summary>
    /// Creates a new document for the specified namespace.
    /// Uses the project's language to determine the file extension and content template.
    /// </summary>
    private static Task<Document> CreateDocumentForNamespaceAsync(Project project, INamespaceSymbol namespaceSymbol, CancellationToken cancellationToken) {
        var namespaceName = namespaceSymbol.ToDisplayString();
        bool isVB = project.Language == LanguageNames.VisualBasic;
        string fileExtension = isVB ? ".vb" : ".cs";

        var fileName = string.IsNullOrEmpty(namespaceName) || namespaceSymbol.IsGlobalNamespace
            ? $"GlobalNamespace{fileExtension}"
            : $"{namespaceName.Split('.').Last()}{fileExtension}";

        // Ensure the file name doesn't conflict with existing files
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;
        var projectDirectory = Path.GetDirectoryName(project.FilePath) ?? throw new InvalidOperationException("Project directory not found");

        var fullPath = Path.Combine(projectDirectory, fileName);
        while (project.Documents.Any(d => string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))) {
            fileName = $"{baseName}{counter}{extension}";
            fullPath = Path.Combine(projectDirectory, fileName);
            counter++;
        }

        // Create basic content for the new file using the correct language syntax
        string content;
        if (isVB) {
            content = namespaceSymbol.IsGlobalNamespace
                ? "' Global namespace file\n"
                : $"Namespace {namespaceName}\n    ' Namespace content\nEnd Namespace\n";
        } else {
            content = namespaceSymbol.IsGlobalNamespace
                ? "// Global namespace file\n"
                : $"namespace {namespaceName} {{\n    // Namespace content\n}}\n";
        }

        var newDocument = project.AddDocument(fileName, content, filePath: fullPath);
        return Task.FromResult(newDocument);
    }
    /// <summary>
    /// Adds a member to the specified namespace in the given document.
    /// </summary>
    private static async Task<Solution> AddMemberToNamespaceAsync(Document document, INamespaceSymbol namespaceSymbol, MemberDeclarationSyntax memberDeclaration, ICodeModificationService modificationService, CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root is not CompilationUnitSyntax compilationUnit) {
            throw new McpException("Destination document does not have a valid compilation unit.");
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken);

        if (namespaceSymbol.IsGlobalNamespace) {
            // Add to global namespace (compilation unit)
            editor.AddMember(compilationUnit, memberDeclaration);
        } else {
            // Find or create the target namespace
            var namespaceName = namespaceSymbol.ToDisplayString();
            var targetNamespace = compilationUnit.Members
                .OfType<NamespaceDeclarationSyntax>()
                .FirstOrDefault(n => n.Name.ToString() == namespaceName);

            if (targetNamespace == null) {
                // Create the namespace and add the member to it
                targetNamespace = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
                    .AddMembers(memberDeclaration);
                editor.AddMember(compilationUnit, targetNamespace);
            } else {
                // Add member to existing namespace
                editor.AddMember(targetNamespace, memberDeclaration);
            }
        }

        var changedDocument = editor.GetChangedDocument();
        var formattedDocument = await modificationService.FormatDocumentAsync(changedDocument, cancellationToken);
        return formattedDocument.Project.Solution;
    }
    /// <summary>
    /// Adds a VB.NET member to the specified namespace in the given document.
    /// </summary>
    private static async Task<Solution> AddMemberToNamespaceVBAsync(Document document, INamespaceSymbol namespaceSymbol, SyntaxNode memberNode, ICodeModificationService modificationService, CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root is not VBSyntax.CompilationUnitSyntax vbCompilationUnit) {
            throw new McpException("Destination VB.NET document does not have a valid compilation unit.");
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken);

        if (namespaceSymbol.IsGlobalNamespace) {
            editor.AddMember(vbCompilationUnit, memberNode);
        } else {
            var namespaceName = namespaceSymbol.ToDisplayString();
            var targetNamespace = vbCompilationUnit.Members
                .OfType<VBSyntax.NamespaceBlockSyntax>()
                .FirstOrDefault(n => n.NamespaceStatement.Name.ToString() == namespaceName);

            if (targetNamespace == null) {
                // Create the namespace and add the member to it
                var nsStatement = VBSyntaxFactory.NamespaceStatement(VBSyntaxFactory.ParseName(namespaceName));
                var newNsBlock = VBSyntaxFactory.NamespaceBlock(nsStatement);
                if (memberNode is VBSyntax.StatementSyntax vbStatement) {
                    newNsBlock = newNsBlock.AddMembers(vbStatement);
                }
                editor.AddMember(vbCompilationUnit, newNsBlock);
            } else {
                editor.AddMember(targetNamespace, memberNode);
            }
        }

        var changedDocument = editor.GetChangedDocument();
        var formattedDocument = await modificationService.FormatDocumentAsync(changedDocument, cancellationToken);
        return formattedDocument.Project.Solution;
    }

    /// <summary>
    /// Properly removes a member from its parent by deleting it from the parent's member collection.
    /// </summary>
    private static async Task<Solution> RemoveMemberFromParentAsync(Document document, SyntaxNode memberNode, ICodeModificationService modificationService, CancellationToken cancellationToken) {
        if (memberNode is not MemberDeclarationSyntax memberDeclaration) {
            throw new McpException($"Node is not a member declaration: {memberNode.GetType().Name}");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) {
            throw new McpException("Could not get syntax root from document.");
        }

        SyntaxNode newRoot;

        if (memberNode.Parent is CompilationUnitSyntax compilationUnit) {
            // Handle top-level members in the compilation unit
            var newMembers = compilationUnit.Members.Remove(memberDeclaration);
            newRoot = compilationUnit.WithMembers(newMembers);
        } else if (memberNode.Parent is NamespaceDeclarationSyntax namespaceDecl) {
            // Handle members in a namespace
            var newMembers = namespaceDecl.Members.Remove(memberDeclaration);
            var newNamespace = namespaceDecl.WithMembers(newMembers);
            newRoot = root.ReplaceNode(namespaceDecl, newNamespace);
        } else if (memberNode.Parent is TypeDeclarationSyntax typeDecl) {
            // Handle members in a type declaration (class, struct, interface, etc.)
            var newMembers = typeDecl.Members.Remove(memberDeclaration);
            var newType = typeDecl.WithMembers(newMembers);
            newRoot = root.ReplaceNode(typeDecl, newType);
        } else {
            throw new McpException($"Cannot remove member from parent of type {memberNode.Parent?.GetType().Name ?? "null"}.");
        }

        var newDocument = document.WithSyntaxRoot(newRoot);
        var formattedDocument = await modificationService.FormatDocumentAsync(newDocument, cancellationToken);
        return formattedDocument.Project.Solution;
    }
    private static async Task<(bool changed, string diff)> ProcessNonCodeFile(
        string filePath,
        string regexPattern,
        string replacementText,
        IDocumentOperationsService documentOperations,
        List<string> modifiedFiles,
        CancellationToken cancellationToken) {
        try {
            var (originalContent, _) = await documentOperations.ReadFileAsync(filePath, false, cancellationToken);
            var regex = new Regex(regexPattern, RegexOptions.Multiline);
            string newContent = regex.Replace(originalContent.NormalizeEndOfLines(), replacementText);

            // Only write if content changed
            if (newContent != originalContent) {
                // Note: we don't pass commit message here as we'll handle Git at a higher level
                // for all modified non-code files at once
                await documentOperations.WriteFileAsync(filePath, newContent, true, cancellationToken, string.Empty);
                modifiedFiles.Add(filePath);

                // Generate diff
                string diff = ContextInjectors.CreateCodeDiff(originalContent, newContent);
                return (true, diff);
            }

            return (false, string.Empty);
        } catch (Exception ex) {
            throw new McpException($"Error processing non-code file {filePath}: {ex.Message}");
        }
    }
}