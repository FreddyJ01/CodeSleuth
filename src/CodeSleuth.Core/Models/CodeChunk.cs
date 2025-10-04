using System;
using System.Collections.Generic;

namespace CodeSleuth.Core.Models;

/// <summary>
/// Represents a semantic chunk of code extracted from a source file.
/// </summary>
public class CodeChunk
{
    /// <summary>
    /// Gets or sets the unique identifier for this code chunk.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the type of code element.
    /// Valid values: "class", "method", "interface", "property", "constructor", "field", "enum"
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the code element.
    /// For methods, this will be the qualified name like "ClassName.MethodName".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full source code content of this element.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file path where this code chunk was found.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the starting line number of this code chunk in the source file.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Gets or sets the ending line number of this code chunk in the source file.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Gets or sets the name of the parent element (e.g., containing class for methods).
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// Gets or sets the namespace containing this code element.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Gets or sets the list of dependencies (using statements, referenced types, etc.).
    /// </summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// Gets or sets the access modifiers for this code element.
    /// </summary>
    public string? AccessModifiers { get; set; }

    /// <summary>
    /// Gets or sets additional metadata about this code chunk.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Returns a string representation of this code chunk.
    /// </summary>
    public override string ToString()
    {
        var parentInfo = !string.IsNullOrEmpty(ParentName) ? $" (in {ParentName})" : "";
        var namespaceInfo = !string.IsNullOrEmpty(Namespace) ? $" [{Namespace}]" : "";
        return $"{Type}: {Name}{parentInfo}{namespaceInfo} - Lines {StartLine}-{EndLine}";
    }
}