using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using CodeSleuth.Core.Services;
using CodeSleuth.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeSleuth.Tests.Unit.Core.Services;

public class CodeParsingServiceTests : IDisposable
{
    private readonly Mock<ILogger<CodeParsingService>> _mockLogger;
    private readonly CodeParsingService _codeParsingService;
    private readonly string _testFilesDirectory;

    public CodeParsingServiceTests()
    {
        _mockLogger = new Mock<ILogger<CodeParsingService>>();
        _codeParsingService = new CodeParsingService(_mockLogger.Object);
        _testFilesDirectory = Path.Combine(Path.GetTempPath(), "CodeParsingServiceTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testFilesDirectory);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CodeParsingService(null!));
    }

    [Fact]
    public void Constructor_WithValidLogger_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var service = new CodeParsingService(_mockLogger.Object);

        // Assert
        Assert.NotNull(service);
        var configInfo = service.GetConfigurationInfo();
        Assert.Contains("CodeParsingService", configInfo);
        Assert.Contains("Roslyn", configInfo);
    }

    [Fact]
    public void ParseCSharpFile_WithNullFilePath_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _codeParsingService.ParseCSharpFile(null!));
    }

    [Fact]
    public void ParseCSharpFile_WithEmptyFilePath_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _codeParsingService.ParseCSharpFile(""));
    }

    [Fact]
    public void ParseCSharpFile_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        const string nonExistentFile = "/path/that/does/not/exist.cs";

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => _codeParsingService.ParseCSharpFile(nonExistentFile));
    }

    [Fact]
    public void ParseCSharpFile_WithSimpleClass_ShouldExtractClassChunk()
    {
        // Arrange
        var sourceCode = @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            Console.WriteLine(""Hello World"");
        }
    }
}";
        var testFile = CreateTestFile("SimpleClass.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        Assert.NotNull(chunks);
        Assert.NotEmpty(chunks);

        var classChunk = chunks.FirstOrDefault(c => c.Type == "class");
        Assert.NotNull(classChunk);
        Assert.Equal("TestNamespace.TestClass", classChunk.Name);
        Assert.Equal("TestNamespace", classChunk.Namespace);
        Assert.Null(classChunk.ParentName);
        Assert.Equal("public", classChunk.AccessModifiers);
        Assert.Contains("System", classChunk.Dependencies);
    }

    [Fact]
    public void ParseCSharpFile_WithMethodInClass_ShouldExtractMethodChunk()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod(string parameter)
        {
            Console.WriteLine(parameter);
        }

        private int Calculate(int a, int b)
        {
            return a + b;
        }
    }
}";
        var testFile = CreateTestFile("ClassWithMethods.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var methodChunks = chunks.Where(c => c.Type == "method").ToList();
        Assert.Equal(2, methodChunks.Count);

        var testMethodChunk = methodChunks.FirstOrDefault(c => c.Name == "TestClass.TestMethod");
        Assert.NotNull(testMethodChunk);
        Assert.Equal("TestNamespace", testMethodChunk.Namespace);
        Assert.Equal("TestClass", testMethodChunk.ParentName);
        Assert.Equal("public", testMethodChunk.AccessModifiers);
        Assert.Contains("void", testMethodChunk.Content);

        var calculateMethodChunk = methodChunks.FirstOrDefault(c => c.Name == "TestClass.Calculate");
        Assert.NotNull(calculateMethodChunk);
        Assert.Equal("private", calculateMethodChunk.AccessModifiers);
    }

    [Fact]
    public void ParseCSharpFile_WithInterface_ShouldExtractInterfaceChunk()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public interface ITestInterface
    {
        void DoSomething();
        string GetValue();
    }
}";
        var testFile = CreateTestFile("TestInterface.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var interfaceChunk = chunks.FirstOrDefault(c => c.Type == "interface");
        Assert.NotNull(interfaceChunk);
        Assert.Equal("TestNamespace.ITestInterface", interfaceChunk.Name);
        Assert.Equal("TestNamespace", interfaceChunk.Namespace);
        Assert.Equal("public", interfaceChunk.AccessModifiers);
    }

    [Fact]
    public void ParseCSharpFile_WithProperties_ShouldExtractPropertyChunks()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string Name { get; set; }
        
        private int _age;
        public int Age 
        { 
            get => _age; 
            set => _age = value; 
        }

        public static readonly string StaticProperty = ""test"";
    }
}";
        var testFile = CreateTestFile("ClassWithProperties.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var propertyChunks = chunks.Where(c => c.Type == "property").ToList();
        Assert.Equal(2, propertyChunks.Count);

        var nameProperty = propertyChunks.FirstOrDefault(c => c.Name == "TestClass.Name");
        Assert.NotNull(nameProperty);
        Assert.Equal("public", nameProperty.AccessModifiers);
        Assert.Equal("TestClass", nameProperty.ParentName);

        var ageProperty = propertyChunks.FirstOrDefault(c => c.Name == "TestClass.Age");
        Assert.NotNull(ageProperty);
        Assert.Equal("public", ageProperty.AccessModifiers);
    }

    [Fact]
    public void ParseCSharpFile_WithConstructor_ShouldExtractConstructorChunk()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public TestClass()
        {
        }

        public TestClass(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public string Name { get; set; }
        public int Age { get; set; }
    }
}";
        var testFile = CreateTestFile("ClassWithConstructors.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var constructorChunks = chunks.Where(c => c.Type == "constructor").ToList();
        Assert.Equal(2, constructorChunks.Count);

        var defaultConstructor = constructorChunks.FirstOrDefault(c => c.Content.Contains("TestClass()"));
        Assert.NotNull(defaultConstructor);
        Assert.Equal("TestClass..ctor", defaultConstructor.Name);
        Assert.Equal("TestClass", defaultConstructor.ParentName);
        Assert.Equal("public", defaultConstructor.AccessModifiers);

        var parameterizedConstructor = constructorChunks.FirstOrDefault(c => c.Content.Contains("string name"));
        Assert.NotNull(parameterizedConstructor);
        Assert.Equal("public", parameterizedConstructor.AccessModifiers);
    }

    [Fact]
    public void ParseCSharpFile_WithField_ShouldExtractFieldChunk()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        private string _name;
        public static readonly int MaxValue = 100;
        private const string DefaultName = ""Default"";
    }
}";
        var testFile = CreateTestFile("ClassWithFields.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var fieldChunks = chunks.Where(c => c.Type == "field").ToList();
        Assert.Equal(3, fieldChunks.Count);

        var nameField = fieldChunks.FirstOrDefault(c => c.Name == "TestClass._name");
        Assert.NotNull(nameField);
        Assert.Equal("private", nameField.AccessModifiers);

        var maxValueField = fieldChunks.FirstOrDefault(c => c.Name == "TestClass.MaxValue");
        Assert.NotNull(maxValueField);
        Assert.Contains("static", maxValueField.AccessModifiers);
        Assert.Contains("readonly", maxValueField.AccessModifiers);

        var defaultNameField = fieldChunks.FirstOrDefault(c => c.Name == "TestClass.DefaultName");
        Assert.NotNull(defaultNameField);
        Assert.Contains("const", defaultNameField.AccessModifiers);
    }

    [Fact]
    public void ParseCSharpFile_WithEnum_ShouldExtractEnumChunk()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public enum Status
    {
        Active,
        Inactive,
        Pending
    }
}";
        var testFile = CreateTestFile("TestEnum.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var enumChunk = chunks.FirstOrDefault(c => c.Type == "enum");
        Assert.NotNull(enumChunk);
        Assert.Equal("TestNamespace.Status", enumChunk.Name);
        Assert.Equal("public", enumChunk.AccessModifiers);
        Assert.True(enumChunk.Metadata.ContainsKey("EnumValues"));
    }

    [Fact]
    public void ParseCSharpFile_WithStruct_ShouldExtractStructChunk()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public struct Point
    {
        public int X { get; set; }
        public int Y { get; set; }

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}";
        var testFile = CreateTestFile("TestStruct.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var structChunk = chunks.FirstOrDefault(c => c.Type == "struct");
        Assert.NotNull(structChunk);
        Assert.Equal("TestNamespace.Point", structChunk.Name);
        Assert.Equal("public", structChunk.AccessModifiers);

        // Should also have properties and constructor
        Assert.Contains(chunks, c => c.Type == "property" && c.ParentName == "Point");
        Assert.Contains(chunks, c => c.Type == "constructor" && c.ParentName == "Point");
    }

    [Fact]
    public void ParseCSharpFile_WithRecord_ShouldExtractRecordChunk()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public record Person(string FirstName, string LastName)
    {
        public int Age { get; set; }
    }
}";
        var testFile = CreateTestFile("TestRecord.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var recordChunk = chunks.FirstOrDefault(c => c.Type == "record");
        Assert.NotNull(recordChunk);
        Assert.Equal("TestNamespace.Person", recordChunk.Name);
        Assert.Equal("public", recordChunk.AccessModifiers);
    }

    [Fact]
    public void ParseCSharpFile_WithNestedClass_ShouldExtractNestedClassChunk()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public class OuterClass
    {
        public class NestedClass
        {
            public void NestedMethod()
            {
            }
        }
    }
}";
        var testFile = CreateTestFile("NestedClass.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var outerClassChunk = chunks.FirstOrDefault(c => c.Type == "class" && c.Name == "TestNamespace.OuterClass");
        Assert.NotNull(outerClassChunk);

        var nestedClassChunk = chunks.FirstOrDefault(c => c.Type == "class" && c.Name == "OuterClass.NestedClass");
        Assert.NotNull(nestedClassChunk);
        Assert.Equal("OuterClass", nestedClassChunk.ParentName);

        var nestedMethodChunk = chunks.FirstOrDefault(c => c.Type == "method" && c.Name == "OuterClass.NestedClass.NestedMethod");
        Assert.NotNull(nestedMethodChunk);
        Assert.Equal("OuterClass.NestedClass", nestedMethodChunk.ParentName);
    }

    [Fact]
    public void ParseCSharpFile_WithSyntaxErrors_ShouldLogWarningsAndContinue()
    {
        // Arrange
        var sourceCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod(
        {
            // Missing closing parenthesis in method declaration
        }
    }
}";
        var testFile = CreateTestFile("SyntaxErrors.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        // Should still extract some chunks despite syntax errors
        Assert.NotNull(chunks);
        // The service should continue parsing even with syntax errors
    }

    [Fact]
    public void ParseCSharpFile_WithComplexFile_ShouldExtractAllChunks()
    {
        // Arrange
        var sourceCode = @"
using System;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ComplexClass : IDisposable
    {
        private readonly string _name;
        public event EventHandler<string> NameChanged;

        public ComplexClass(string name)
        {
            _name = name;
        }

        public string Name 
        { 
            get => _name;
            set 
            {
                _name = value;
                NameChanged?.Invoke(this, value);
            }
        }

        public void DoSomething()
        {
            Console.WriteLine(_name);
        }

        public void Dispose()
        {
            // Cleanup
        }
    }

    public interface ITestInterface
    {
        void TestMethod();
    }

    public enum TestEnum
    {
        Value1,
        Value2
    }
}";
        var testFile = CreateTestFile("ComplexFile.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        Assert.NotNull(chunks);
        Assert.True(chunks.Count >= 8); // At least class, field, event, constructor, property, 2 methods, interface, enum

        // Verify all types are present
        Assert.Contains(chunks, c => c.Type == "class");
        Assert.Contains(chunks, c => c.Type == "constructor");
        Assert.Contains(chunks, c => c.Type == "property");
        Assert.Contains(chunks, c => c.Type == "method");
        Assert.Contains(chunks, c => c.Type == "interface");
        Assert.Contains(chunks, c => c.Type == "enum");
        Assert.Contains(chunks, c => c.Type == "field");
        Assert.Contains(chunks, c => c.Type == "event");

        // Verify all chunks have valid line numbers
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.StartLine > 0);
            Assert.True(chunk.EndLine >= chunk.StartLine);
            Assert.Equal(testFile, chunk.FilePath);
        }
    }

    [Fact]
    public void ParseCSharpFile_WithTopLevelProgram_ShouldHandleCorrectly()
    {
        // Arrange
        var sourceCode = @"
using System;

Console.WriteLine(""Hello World"");

public class TopLevelClass
{
    public void Method()
    {
    }
}";
        var testFile = CreateTestFile("TopLevelProgram.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        Assert.NotNull(chunks);
        var classChunk = chunks.FirstOrDefault(c => c.Type == "class");
        Assert.NotNull(classChunk);
        Assert.Equal("TopLevelClass", classChunk.Name);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("protected")]
    [InlineData("internal")]
    [InlineData("public static")]
    [InlineData("private readonly")]
    public void ParseCSharpFile_WithVariousAccessModifiers_ShouldExtractCorrectly(string accessModifier)
    {
        // Arrange
        var sourceCode = $@"
namespace TestNamespace
{{
    public class TestClass
    {{
        {accessModifier} string TestField = ""test"";
    }}
}}";
        var testFile = CreateTestFile($"AccessModifier_{accessModifier.Replace(" ", "_")}.cs", sourceCode);

        // Act
        var chunks = _codeParsingService.ParseCSharpFile(testFile);

        // Assert
        var fieldChunk = chunks.FirstOrDefault(c => c.Type == "field");
        Assert.NotNull(fieldChunk);
        Assert.Equal(accessModifier, fieldChunk.AccessModifiers);
    }

    private string CreateTestFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testFilesDirectory, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testFilesDirectory))
            {
                Directory.Delete(_testFilesDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}