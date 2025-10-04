using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using CodeSleuth.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeSleuth.Core.Services;

/// <summary>
/// Custom exception for code parsing operations.
/// </summary>
public class CodeParsingException : Exception
{
    public CodeParsingException(string message) : base(message) { }
    public CodeParsingException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Service for parsing C# source code into semantic chunks using Roslyn.
/// Extracts classes, methods, interfaces, properties, and other code elements.
/// </summary>
public class CodeParsingService
{
    private readonly ILogger<CodeParsingService> _logger;

    /// <summary>
    /// Initializes a new instance of the CodeParsingService class.
    /// </summary>
    /// <param name="logger">The logger instance for logging operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public CodeParsingService(ILogger<CodeParsingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("CodeParsingService initialized");
    }

    /// <summary>
    /// Parses a C# source file and extracts semantic code chunks.
    /// </summary>
    /// <param name="filePath">The path to the C# file to parse.</param>
    /// <returns>A list of code chunks extracted from the file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file doesn't exist.</exception>
    /// <exception cref="CodeParsingException">Thrown when parsing fails.</exception>
    public List<CodeChunk> ParseCSharpFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File does not exist: {filePath}");

        try
        {
            _logger.LogDebug("Parsing C# file: {FilePath}", filePath);

            var sourceText = File.ReadAllText(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
            var root = syntaxTree.GetCompilationUnitRoot();

            // Check for syntax errors
            var diagnostics = syntaxTree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            if (diagnostics.Any())
            {
                _logger.LogWarning("Syntax errors found in {FilePath}: {ErrorCount} errors", 
                    filePath, diagnostics.Count);
                
                foreach (var diagnostic in diagnostics.Take(5)) // Log first 5 errors
                {
                    _logger.LogWarning("Syntax error: {Message} at {Location}", 
                        diagnostic.GetMessage(), diagnostic.Location);
                }

                // Continue parsing despite errors, but log them
            }

            var chunks = new List<CodeChunk>();
            var context = new ParsingContext(filePath, sourceText, syntaxTree);

            // Extract using directives for dependencies
            var usings = root.Usings.Select(u => u.Name?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();

            // Parse namespace declarations
            foreach (var namespaceDeclaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                ParseNamespaceDeclaration(namespaceDeclaration, context, chunks, usings);
            }

            // Parse top-level declarations (for file-scoped namespaces or global declarations)
            ParseTopLevelDeclarations(root, context, chunks, usings, null);

            _logger.LogInformation("Successfully parsed {FilePath}: {ChunkCount} chunks extracted", 
                filePath, chunks.Count);

            return chunks;
        }
        catch (Exception ex) when (!(ex is ArgumentNullException or FileNotFoundException))
        {
            _logger.LogError(ex, "Failed to parse C# file: {FilePath}", filePath);
            throw new CodeParsingException($"Failed to parse C# file '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parses declarations within a namespace.
    /// </summary>
    private void ParseNamespaceDeclaration(BaseNamespaceDeclarationSyntax namespaceDeclaration, 
        ParsingContext context, List<CodeChunk> chunks, List<string> usings)
    {
        var namespaceName = namespaceDeclaration.Name.ToString();
        _logger.LogTrace("Parsing namespace: {NamespaceName}", namespaceName);

        ParseTopLevelDeclarations(namespaceDeclaration, context, chunks, usings, namespaceName);
    }

    /// <summary>
    /// Parses top-level declarations (classes, interfaces, enums, etc.).
    /// </summary>
    private void ParseTopLevelDeclarations(SyntaxNode parent, ParsingContext context, 
        List<CodeChunk> chunks, List<string> usings, string? namespaceName)
    {
        var typeDeclarations = parent.ChildNodes().OfType<BaseTypeDeclarationSyntax>();

        foreach (var typeDeclaration in typeDeclarations)
        {
            switch (typeDeclaration)
            {
                case ClassDeclarationSyntax classDecl:
                    ParseClassDeclaration(classDecl, context, chunks, usings, namespaceName);
                    break;
                case InterfaceDeclarationSyntax interfaceDecl:
                    ParseInterfaceDeclaration(interfaceDecl, context, chunks, usings, namespaceName);
                    break;
                case StructDeclarationSyntax structDecl:
                    ParseStructDeclaration(structDecl, context, chunks, usings, namespaceName);
                    break;
                case EnumDeclarationSyntax enumDecl:
                    ParseEnumDeclaration(enumDecl, context, chunks, usings, namespaceName);
                    break;
                case RecordDeclarationSyntax recordDecl:
                    ParseRecordDeclaration(recordDecl, context, chunks, usings, namespaceName);
                    break;
            }
        }
    }

    /// <summary>
    /// Parses a class declaration and its members.
    /// </summary>
    private void ParseClassDeclaration(ClassDeclarationSyntax classDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName)
    {
        var className = classDeclaration.Identifier.ValueText;
        var qualifiedClassName = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}.{className}" : className;

        _logger.LogTrace("Parsing class: {ClassName}", qualifiedClassName);

        // Create chunk for the class itself
        var classChunk = CreateCodeChunk(
            type: "class",
            name: qualifiedClassName,
            content: classDeclaration.ToString(),
            context: context,
            syntaxNode: classDeclaration,
            namespaceName: namespaceName,
            parentName: null,
            dependencies: usings
        );

        classChunk.AccessModifiers = GetAccessModifiers(classDeclaration.Modifiers);
        chunks.Add(classChunk);

        // Parse class members
        ParseClassMembers(classDeclaration, context, chunks, usings, namespaceName, className);
    }

    /// <summary>
    /// Parses members within a class.
    /// </summary>
    private void ParseClassMembers(BaseTypeDeclarationSyntax typeDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string typeName)
    {
        // Cast to TypeDeclarationSyntax to access Members
        if (typeDeclaration is not TypeDeclarationSyntax typeDecl)
            return;

        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    ParseMethodDeclaration(method, context, chunks, usings, namespaceName, typeName);
                    break;
                case PropertyDeclarationSyntax property:
                    ParsePropertyDeclaration(property, context, chunks, usings, namespaceName, typeName);
                    break;
                case ConstructorDeclarationSyntax constructor:
                    ParseConstructorDeclaration(constructor, context, chunks, usings, namespaceName, typeName);
                    break;
                case FieldDeclarationSyntax field:
                    ParseFieldDeclaration(field, context, chunks, usings, namespaceName, typeName);
                    break;
                case EventDeclarationSyntax eventDecl:
                    ParseEventDeclaration(eventDecl, context, chunks, usings, namespaceName, typeName);
                    break;
                case EventFieldDeclarationSyntax eventField:
                    ParseEventFieldDeclaration(eventField, context, chunks, usings, namespaceName, typeName);
                    break;
                case IndexerDeclarationSyntax indexer:
                    ParseIndexerDeclaration(indexer, context, chunks, usings, namespaceName, typeName);
                    break;
                // Handle nested types
                case BaseTypeDeclarationSyntax nestedType:
                    ParseNestedTypeDeclaration(nestedType, context, chunks, usings, namespaceName, typeName);
                    break;
            }
        }
    }

    /// <summary>
    /// Parses a method declaration.
    /// </summary>
    private void ParseMethodDeclaration(MethodDeclarationSyntax methodDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var methodName = methodDeclaration.Identifier.ValueText;
        var qualifiedMethodName = $"{parentName}.{methodName}";

        var methodChunk = CreateCodeChunk(
            type: "method",
            name: qualifiedMethodName,
            content: methodDeclaration.ToString(),
            context: context,
            syntaxNode: methodDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        methodChunk.AccessModifiers = GetAccessModifiers(methodDeclaration.Modifiers);
        
        // Add parameter types to metadata
        var parameters = methodDeclaration.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "unknown")
            .ToList();
        
        if (parameters.Any())
        {
            methodChunk.Metadata["Parameters"] = parameters;
        }

        // Add return type to metadata
        methodChunk.Metadata["ReturnType"] = methodDeclaration.ReturnType.ToString();

        chunks.Add(methodChunk);
    }

    /// <summary>
    /// Parses a property declaration.
    /// </summary>
    private void ParsePropertyDeclaration(PropertyDeclarationSyntax propertyDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var propertyName = propertyDeclaration.Identifier.ValueText;
        var qualifiedPropertyName = $"{parentName}.{propertyName}";

        var propertyChunk = CreateCodeChunk(
            type: "property",
            name: qualifiedPropertyName,
            content: propertyDeclaration.ToString(),
            context: context,
            syntaxNode: propertyDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        propertyChunk.AccessModifiers = GetAccessModifiers(propertyDeclaration.Modifiers);
        propertyChunk.Metadata["PropertyType"] = propertyDeclaration.Type.ToString();

        chunks.Add(propertyChunk);
    }

    /// <summary>
    /// Parses a constructor declaration.
    /// </summary>
    private void ParseConstructorDeclaration(ConstructorDeclarationSyntax constructorDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var qualifiedConstructorName = $"{parentName}..ctor";

        var constructorChunk = CreateCodeChunk(
            type: "constructor",
            name: qualifiedConstructorName,
            content: constructorDeclaration.ToString(),
            context: context,
            syntaxNode: constructorDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        constructorChunk.AccessModifiers = GetAccessModifiers(constructorDeclaration.Modifiers);

        // Add parameter types to metadata
        var parameters = constructorDeclaration.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "unknown")
            .ToList();
        
        if (parameters.Any())
        {
            constructorChunk.Metadata["Parameters"] = parameters;
        }

        chunks.Add(constructorChunk);
    }

    /// <summary>
    /// Parses a field declaration.
    /// </summary>
    private void ParseFieldDeclaration(FieldDeclarationSyntax fieldDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            var fieldName = variable.Identifier.ValueText;
            var qualifiedFieldName = $"{parentName}.{fieldName}";

            var fieldChunk = CreateCodeChunk(
                type: "field",
                name: qualifiedFieldName,
                content: fieldDeclaration.ToString(),
                context: context,
                syntaxNode: fieldDeclaration,
                namespaceName: namespaceName,
                parentName: parentName,
                dependencies: usings
            );

            fieldChunk.AccessModifiers = GetAccessModifiers(fieldDeclaration.Modifiers);
            fieldChunk.Metadata["FieldType"] = fieldDeclaration.Declaration.Type.ToString();

            chunks.Add(fieldChunk);
        }
    }

    /// <summary>
    /// Parses an event declaration.
    /// </summary>
    private void ParseEventDeclaration(EventDeclarationSyntax eventDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var eventName = eventDeclaration.Identifier.ValueText;
        var qualifiedEventName = $"{parentName}.{eventName}";

        var eventChunk = CreateCodeChunk(
            type: "event",
            name: qualifiedEventName,
            content: eventDeclaration.ToString(),
            context: context,
            syntaxNode: eventDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        eventChunk.AccessModifiers = GetAccessModifiers(eventDeclaration.Modifiers);
        eventChunk.Metadata["EventType"] = eventDeclaration.Type.ToString();

        chunks.Add(eventChunk);
    }

    /// <summary>
    /// Parses an event field declaration (e.g., public event EventHandler MyEvent;).
    /// </summary>
    private void ParseEventFieldDeclaration(EventFieldDeclarationSyntax eventFieldDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        foreach (var variable in eventFieldDeclaration.Declaration.Variables)
        {
            var eventName = variable.Identifier.ValueText;
            var qualifiedEventName = $"{parentName}.{eventName}";

            var eventChunk = CreateCodeChunk(
                type: "event",
                name: qualifiedEventName,
                content: eventFieldDeclaration.ToString(),
                context: context,
                syntaxNode: eventFieldDeclaration,
                namespaceName: namespaceName,
                parentName: parentName,
                dependencies: usings
            );

            eventChunk.AccessModifiers = GetAccessModifiers(eventFieldDeclaration.Modifiers);
            eventChunk.Metadata["EventType"] = eventFieldDeclaration.Declaration.Type.ToString();

            chunks.Add(eventChunk);
        }
    }

    /// <summary>
    /// Parses an indexer declaration.
    /// </summary>
    private void ParseIndexerDeclaration(IndexerDeclarationSyntax indexerDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var qualifiedIndexerName = $"{parentName}.this[]";

        var indexerChunk = CreateCodeChunk(
            type: "indexer",
            name: qualifiedIndexerName,
            content: indexerDeclaration.ToString(),
            context: context,
            syntaxNode: indexerDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        indexerChunk.AccessModifiers = GetAccessModifiers(indexerDeclaration.Modifiers);
        indexerChunk.Metadata["IndexerType"] = indexerDeclaration.Type.ToString();

        chunks.Add(indexerChunk);
    }

    /// <summary>
    /// Parses an interface declaration.
    /// </summary>
    private void ParseInterfaceDeclaration(InterfaceDeclarationSyntax interfaceDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName)
    {
        var interfaceName = interfaceDeclaration.Identifier.ValueText;
        var qualifiedInterfaceName = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}.{interfaceName}" : interfaceName;

        var interfaceChunk = CreateCodeChunk(
            type: "interface",
            name: qualifiedInterfaceName,
            content: interfaceDeclaration.ToString(),
            context: context,
            syntaxNode: interfaceDeclaration,
            namespaceName: namespaceName,
            parentName: null,
            dependencies: usings
        );

        interfaceChunk.AccessModifiers = GetAccessModifiers(interfaceDeclaration.Modifiers);
        chunks.Add(interfaceChunk);

        // Parse interface members
        ParseClassMembers(interfaceDeclaration, context, chunks, usings, namespaceName, interfaceName);
    }

    /// <summary>
    /// Parses a struct declaration.
    /// </summary>
    private void ParseStructDeclaration(StructDeclarationSyntax structDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName)
    {
        var structName = structDeclaration.Identifier.ValueText;
        var qualifiedStructName = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}.{structName}" : structName;

        var structChunk = CreateCodeChunk(
            type: "struct",
            name: qualifiedStructName,
            content: structDeclaration.ToString(),
            context: context,
            syntaxNode: structDeclaration,
            namespaceName: namespaceName,
            parentName: null,
            dependencies: usings
        );

        structChunk.AccessModifiers = GetAccessModifiers(structDeclaration.Modifiers);
        chunks.Add(structChunk);

        // Parse struct members
        ParseClassMembers(structDeclaration, context, chunks, usings, namespaceName, structName);
    }

    /// <summary>
    /// Parses an enum declaration.
    /// </summary>
    private void ParseEnumDeclaration(EnumDeclarationSyntax enumDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName)
    {
        var enumName = enumDeclaration.Identifier.ValueText;
        var qualifiedEnumName = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}.{enumName}" : enumName;

        var enumChunk = CreateCodeChunk(
            type: "enum",
            name: qualifiedEnumName,
            content: enumDeclaration.ToString(),
            context: context,
            syntaxNode: enumDeclaration,
            namespaceName: namespaceName,
            parentName: null,
            dependencies: usings
        );

        enumChunk.AccessModifiers = GetAccessModifiers(enumDeclaration.Modifiers);
        
        // Add enum values to metadata
        var enumValues = enumDeclaration.Members.Select(m => m.Identifier.ValueText).ToList();
        if (enumValues.Any())
        {
            enumChunk.Metadata["EnumValues"] = enumValues;
        }

        chunks.Add(enumChunk);
    }

    /// <summary>
    /// Parses a record declaration.
    /// </summary>
    private void ParseRecordDeclaration(RecordDeclarationSyntax recordDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName)
    {
        var recordName = recordDeclaration.Identifier.ValueText;
        var qualifiedRecordName = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}.{recordName}" : recordName;

        var recordChunk = CreateCodeChunk(
            type: "record",
            name: qualifiedRecordName,
            content: recordDeclaration.ToString(),
            context: context,
            syntaxNode: recordDeclaration,
            namespaceName: namespaceName,
            parentName: null,
            dependencies: usings
        );

        recordChunk.AccessModifiers = GetAccessModifiers(recordDeclaration.Modifiers);
        chunks.Add(recordChunk);

        // Parse record members
        ParseClassMembers(recordDeclaration, context, chunks, usings, namespaceName, recordName);
    }

    /// <summary>
    /// Parses nested type declarations.
    /// </summary>
    private void ParseNestedTypeDeclaration(BaseTypeDeclarationSyntax nestedTypeDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        switch (nestedTypeDeclaration)
        {
            case ClassDeclarationSyntax nestedClass:
                ParseNestedClassDeclaration(nestedClass, context, chunks, usings, namespaceName, parentName);
                break;
            case InterfaceDeclarationSyntax nestedInterface:
                ParseNestedInterfaceDeclaration(nestedInterface, context, chunks, usings, namespaceName, parentName);
                break;
            case StructDeclarationSyntax nestedStruct:
                ParseNestedStructDeclaration(nestedStruct, context, chunks, usings, namespaceName, parentName);
                break;
            case EnumDeclarationSyntax nestedEnum:
                ParseNestedEnumDeclaration(nestedEnum, context, chunks, usings, namespaceName, parentName);
                break;
        }
    }

    /// <summary>
    /// Parses a nested class declaration.
    /// </summary>
    private void ParseNestedClassDeclaration(ClassDeclarationSyntax nestedClassDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var nestedClassName = nestedClassDeclaration.Identifier.ValueText;
        var qualifiedNestedClassName = $"{parentName}.{nestedClassName}";

        var nestedClassChunk = CreateCodeChunk(
            type: "class",
            name: qualifiedNestedClassName,
            content: nestedClassDeclaration.ToString(),
            context: context,
            syntaxNode: nestedClassDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        nestedClassChunk.AccessModifiers = GetAccessModifiers(nestedClassDeclaration.Modifiers);
        chunks.Add(nestedClassChunk);

        // Parse nested class members
        ParseClassMembers(nestedClassDeclaration, context, chunks, usings, namespaceName, qualifiedNestedClassName);
    }

    /// <summary>
    /// Parses a nested interface declaration.
    /// </summary>
    private void ParseNestedInterfaceDeclaration(InterfaceDeclarationSyntax nestedInterfaceDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var nestedInterfaceName = nestedInterfaceDeclaration.Identifier.ValueText;
        var qualifiedNestedInterfaceName = $"{parentName}.{nestedInterfaceName}";

        var nestedInterfaceChunk = CreateCodeChunk(
            type: "interface",
            name: qualifiedNestedInterfaceName,
            content: nestedInterfaceDeclaration.ToString(),
            context: context,
            syntaxNode: nestedInterfaceDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        nestedInterfaceChunk.AccessModifiers = GetAccessModifiers(nestedInterfaceDeclaration.Modifiers);
        chunks.Add(nestedInterfaceChunk);

        // Parse nested interface members
        ParseClassMembers(nestedInterfaceDeclaration, context, chunks, usings, namespaceName, qualifiedNestedInterfaceName);
    }

    /// <summary>
    /// Parses a nested struct declaration.
    /// </summary>
    private void ParseNestedStructDeclaration(StructDeclarationSyntax nestedStructDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var nestedStructName = nestedStructDeclaration.Identifier.ValueText;
        var qualifiedNestedStructName = $"{parentName}.{nestedStructName}";

        var nestedStructChunk = CreateCodeChunk(
            type: "struct",
            name: qualifiedNestedStructName,
            content: nestedStructDeclaration.ToString(),
            context: context,
            syntaxNode: nestedStructDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        nestedStructChunk.AccessModifiers = GetAccessModifiers(nestedStructDeclaration.Modifiers);
        chunks.Add(nestedStructChunk);

        // Parse nested struct members
        ParseClassMembers(nestedStructDeclaration, context, chunks, usings, namespaceName, qualifiedNestedStructName);
    }

    /// <summary>
    /// Parses a nested enum declaration.
    /// </summary>
    private void ParseNestedEnumDeclaration(EnumDeclarationSyntax nestedEnumDeclaration, ParsingContext context,
        List<CodeChunk> chunks, List<string> usings, string? namespaceName, string parentName)
    {
        var nestedEnumName = nestedEnumDeclaration.Identifier.ValueText;
        var qualifiedNestedEnumName = $"{parentName}.{nestedEnumName}";

        var nestedEnumChunk = CreateCodeChunk(
            type: "enum",
            name: qualifiedNestedEnumName,
            content: nestedEnumDeclaration.ToString(),
            context: context,
            syntaxNode: nestedEnumDeclaration,
            namespaceName: namespaceName,
            parentName: parentName,
            dependencies: usings
        );

        nestedEnumChunk.AccessModifiers = GetAccessModifiers(nestedEnumDeclaration.Modifiers);
        
        // Add enum values to metadata
        var enumValues = nestedEnumDeclaration.Members.Select(m => m.Identifier.ValueText).ToList();
        if (enumValues.Any())
        {
            nestedEnumChunk.Metadata["EnumValues"] = enumValues;
        }

        chunks.Add(nestedEnumChunk);
    }

    /// <summary>
    /// Creates a code chunk with common properties.
    /// </summary>
    private CodeChunk CreateCodeChunk(string type, string name, string content, ParsingContext context,
        SyntaxNode syntaxNode, string? namespaceName, string? parentName, List<string> dependencies)
    {
        var location = syntaxNode.GetLocation();
        var lineSpan = location.GetLineSpan();

        return new CodeChunk
        {
            Id = Guid.NewGuid(),
            Type = type,
            Name = name,
            Content = content,
            FilePath = context.FilePath,
            StartLine = lineSpan.StartLinePosition.Line + 1, // Convert to 1-based line numbers
            EndLine = lineSpan.EndLinePosition.Line + 1,
            ParentName = parentName,
            Namespace = namespaceName,
            Dependencies = new List<string>(dependencies)
        };
    }

    /// <summary>
    /// Extracts access modifiers from a syntax token list.
    /// </summary>
    private static string GetAccessModifiers(SyntaxTokenList modifiers)
    {
        var accessModifiers = modifiers
            .Where(token => token.IsKind(SyntaxKind.PublicKeyword) ||
                           token.IsKind(SyntaxKind.PrivateKeyword) ||
                           token.IsKind(SyntaxKind.ProtectedKeyword) ||
                           token.IsKind(SyntaxKind.InternalKeyword) ||
                           token.IsKind(SyntaxKind.StaticKeyword) ||
                           token.IsKind(SyntaxKind.AbstractKeyword) ||
                           token.IsKind(SyntaxKind.VirtualKeyword) ||
                           token.IsKind(SyntaxKind.OverrideKeyword) ||
                           token.IsKind(SyntaxKind.SealedKeyword) ||
                           token.IsKind(SyntaxKind.ReadOnlyKeyword) ||
                           token.IsKind(SyntaxKind.ConstKeyword))
            .Select(token => token.ValueText)
            .ToList();

        return string.Join(" ", accessModifiers);
    }

    /// <summary>
    /// Gets information about the current parsing service configuration.
    /// </summary>
    /// <returns>Configuration information for debugging purposes.</returns>
    public string GetConfigurationInfo()
    {
        return "CodeParsingService - Using Microsoft.CodeAnalysis.CSharp (Roslyn) for C# parsing";
    }
}

/// <summary>
/// Context information for parsing operations.
/// </summary>
internal class ParsingContext
{
    public string FilePath { get; }
    public string SourceText { get; }
    public SyntaxTree SyntaxTree { get; }

    public ParsingContext(string filePath, string sourceText, SyntaxTree syntaxTree)
    {
        FilePath = filePath;
        SourceText = sourceText;
        SyntaxTree = syntaxTree;
    }
}
