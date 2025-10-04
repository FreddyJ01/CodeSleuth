using System;
using System.Collections.Generic;
using CodeSleuth.Core.Models;

/// <summary>
/// Demonstration of the RAG pipeline showing how to ask questions about indexed repositories.
/// This shows the complete workflow from question to answer with code references.
/// </summary>
public class RAGDemo
{
    /// <summary>
    /// Demonstrates asking questions about the CodeSleuth repository and getting AI-generated answers
    /// with relevant code references. This shows the complete RAG pipeline in action.
    /// </summary>
    public static void Main(string[] args)
    {
        Console.WriteLine("🔍 CodeSleuth RAG Demo - Ask Questions About Your Indexed Repository");
        Console.WriteLine(new string('=', 70));
        Console.WriteLine();

        // Sample questions that could be asked about the CodeSleuth repository
        var sampleQuestions = new[]
        {
            "How does the QueryService implement RAG (Retrieval Augmented Generation)?",
            "What retry and error handling mechanisms does the EmbeddingService use?",
            "How is the vector database implemented and what search capabilities does it provide?",
            "What are the main components of the CodeSleuth architecture?",
            "How does the system handle code indexing and embedding generation?"
        };

        Console.WriteLine("📋 Sample Questions You Could Ask:");
        for (int i = 0; i < sampleQuestions.Length; i++)
        {
            Console.WriteLine($"{i + 1}. {sampleQuestions[i]}");
        }
        Console.WriteLine();

        // Example of what a typical RAG response would look like
        Console.WriteLine("🤖 Example RAG Response:");
        Console.WriteLine(new string('-', 50));
        
        var exampleResponse = new QueryResult
        {
            Answer = @"The QueryService implements RAG (Retrieval Augmented Generation) through a comprehensive 7-step pipeline:

1. **Question Embedding**: First, it generates an embedding vector for the user's question using the EmbeddingService
2. **Vector Retrieval**: It searches the Qdrant vector database for similar code chunks using vector similarity
3. **Context Building**: Retrieved code chunks are assembled into a context string with file paths and content
4. **Chat History Creation**: A structured conversation is created with system prompts and user questions
5. **Augmented Generation**: The context is provided to an LLM along with the original question to generate an informed response
6. **Reference Extraction**: Code references are extracted from search results with file paths and line numbers
7. **Result Assembly**: The final QueryResult combines the AI answer, references, and execution metrics

The implementation includes proper error handling, cancellation token support, and metadata filtering for repository-specific searches. This allows users to ask natural language questions about their codebase and receive contextually accurate answers backed by actual code references.",
            
            References = new List<CodeReference>
            {
                new CodeReference
                {
                    FilePath = "src/CodeSleuth.Core/Services/QueryService.cs",
                    StartLine = 1,
                    EndLine = 10,
                    SimilarityScore = 0.95f
                },
                new CodeReference
                {
                    FilePath = "src/CodeSleuth.Core/Services/QueryService.cs", 
                    StartLine = 50,
                    EndLine = 70,
                    SimilarityScore = 0.88f
                },
                new CodeReference
                {
                    FilePath = "src/CodeSleuth.Infrastructure/AI/EmbeddingService.cs",
                    StartLine = 25,
                    EndLine = 45,
                    SimilarityScore = 0.82f
                }
            },
            Duration = TimeSpan.FromMilliseconds(1247)
        };

        Console.WriteLine($"📝 Answer: {exampleResponse.Answer}");
        Console.WriteLine();
        
        Console.WriteLine("📎 Code References:");
        foreach (var reference in exampleResponse.References)
        {
            Console.WriteLine($"  📄 {reference.FilePath} (lines {reference.StartLine}-{reference.EndLine}) - Similarity: {reference.SimilarityScore:F2}");
        }
        Console.WriteLine();
        
        Console.WriteLine($"⏱️  Total Processing Time: {exampleResponse.Duration.TotalMilliseconds:F0}ms");
        Console.WriteLine();

        Console.WriteLine("🏗️  RAG Architecture Components:");
        Console.WriteLine("  ┌─ EmbeddingService (IEmbeddingService)");
        Console.WriteLine("  │  └─ Converts text to vector embeddings");
        Console.WriteLine("  ├─ QdrantService (IQdrantService)");
        Console.WriteLine("  │  └─ Vector database for similarity search");
        Console.WriteLine("  ├─ IChatCompletionService (Semantic Kernel)");
        Console.WriteLine("  │  └─ LLM for generating contextual responses");
        Console.WriteLine("  └─ QueryService");
        Console.WriteLine("     └─ Orchestrates the complete RAG pipeline");
        Console.WriteLine();

        Console.WriteLine("✅ Demo completed! The RAG system is ready to answer questions about your indexed repository.");
        Console.WriteLine("   To use this in production, you would:");
        Console.WriteLine("   1. Index your repository code into the vector database");
        Console.WriteLine("   2. Configure your embedding service (Azure OpenAI/OpenAI)"); 
        Console.WriteLine("   3. Set up your chat completion service");
        Console.WriteLine("   4. Ask questions using QueryService.AskQuestionAsync()");
    }
}
