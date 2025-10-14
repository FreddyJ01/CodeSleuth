using System;
using System.Collections.Generic;
using System.Text;

class Program
{
    private const int MaxTokensPerRequest = 6000;
    private const int EstimatedCharsPerToken = 3;

    static void Main()
    {
        // Test with a very large string (simulate 16376 tokens worth of content)
        var largeText = new StringBuilder();
        int targetChars = 16376 * EstimatedCharsPerToken; // About 49,128 characters
        
        for (int i = 0; i < targetChars / 100; i++)
        {
            largeText.AppendLine($"This is line {i} of test code that simulates a very large code chunk that exceeds token limits.");
        }

        var testText = largeText.ToString();
        Console.WriteLine($"Original text length: {testText.Length} characters");
        Console.WriteLine($"Estimated tokens: {EstimateTokenCount(testText)}");
        
        var chunks = SplitTextIntoChunks(testText);
        Console.WriteLine($"Split into {chunks.Count} chunks");
        
        foreach (var chunk in chunks)
        {
            var estimatedTokens = EstimateTokenCount(chunk);
            Console.WriteLine($"Chunk length: {chunk.Length} chars, estimated tokens: {estimatedTokens}");
            
            if (estimatedTokens > MaxTokensPerRequest)
            {
                Console.WriteLine("ERROR: Chunk still exceeds token limit!");
            }
        }
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        return (int)Math.Ceiling((double)text.Length / EstimatedCharsPerToken);
    }

    private static List<string> SplitTextIntoChunks(string text)
    {
        var chunks = new List<string>();
        
        if (string.IsNullOrEmpty(text))
            return chunks;

        var estimatedTokens = EstimateTokenCount(text);
        
        if (estimatedTokens <= MaxTokensPerRequest)
        {
            chunks.Add(text);
            return chunks;
        }

        var maxCharsPerChunk = MaxTokensPerRequest * EstimatedCharsPerToken;
        
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        var currentChunk = new List<string>();
        var currentChunkLength = 0;

        foreach (var line in lines)
        {
            var lineLength = line.Length + 1;
            
            if (currentChunkLength + lineLength > maxCharsPerChunk && currentChunk.Count > 0)
            {
                chunks.Add(string.Join("\n", currentChunk));
                currentChunk.Clear();
                currentChunkLength = 0;
            }
            
            if (lineLength > maxCharsPerChunk)
            {
                var lineParts = SplitLongLine(line, maxCharsPerChunk);
                chunks.AddRange(lineParts);
            }
            else
            {
                currentChunk.Add(line);
                currentChunkLength += lineLength;
            }
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join("\n", currentChunk));
        }

        return chunks;
    }

    private static List<string> SplitLongLine(string line, int maxChars)
    {
        var parts = new List<string>();
        
        if (line.Length <= maxChars)
        {
            parts.Add(line);
            return parts;
        }

        for (int i = 0; i < line.Length; i += maxChars)
        {
            var end = Math.Min(i + maxChars, line.Length);
            parts.Add(line.Substring(i, end - i));
        }

        return parts;
    }
}
