# RAG Strategies Guide

## Understanding RAG (Retrieval-Augmented Generation)

RAG combines the power of information retrieval with large language models to provide accurate, context-aware answers based on your documents. Instead of relying solely on the AI's training data, RAG searches your uploaded documents for relevant information and uses it to generate answers.

## How It Works (General Flow)

1. **Document Upload** ? Your documents are split into chunks
2. **Embedding** ? Each chunk is converted into a mathematical vector (embedding)
3. **Storage** ? Embeddings are stored in an in-memory vector database
4. **Query** ? Your question is also converted to an embedding
5. **Retrieval** ? The most similar document chunks are found
6. **Generation** ? The AI generates an answer using the retrieved context

---

## The Three RAG Strategies

### 1. ?? Naive RAG (Baseline)

**How it works:**
```
Query ? Embed ? Find Similar Chunks ? Generate Answer
```

**Algorithm:**
1. Convert your query into an embedding vector
2. Calculate cosine similarity between query and all chunk embeddings
3. Return the top-K most similar chunks
4. Generate answer using these chunks as context

**Best for:**
- ? Straightforward factual questions
- ? Questions with clear keyword matches
- ? Well-structured documents with explicit information

**Example scenarios:**
- "What is the pricing model mentioned in the document?"
- "List all the features described"
- "When was this product released?"

**Pros:**
- Fast and efficient
- Predictable results
- Low computational cost

**Cons:**
- Vocabulary mismatch issues (query terms ? document terms)
- Limited understanding of synonyms
- May miss relevant context if phrasing differs

**When to use:** Start here! It works well for most straightforward questions and is the fastest strategy.

---

### 2. ?? Semantic RAG (Enhanced)

**How it works:**
```
Query ? Expand (synonyms/related) ? Multiple Embeddings ? 
Merge Results ? Rerank ? Generate Answer
```

**Algorithm:**
1. Expand query into variations:
   - Original: "pricing"
   - Expanded: "pricing", "What is pricing?", "How does pricing work?", "Examples of pricing"
2. Embed each variation separately
3. Search for each variation in parallel
4. Merge and deduplicate results
5. Rerank by relevance (chunks appearing in multiple searches rank higher)
6. Generate answer from top-ranked chunks

**Best for:**
- ? Complex, nuanced questions
- ? Questions needing broader context
- ? Queries where exact terms aren't in documents
- ? Multi-faceted questions

**Example scenarios:**
- "How does this compare to competitors?" (needs contextual understanding)
- "What are the benefits?" (benefits might be described without using the word "benefit")
- "Explain the architecture" (may be described in various ways)

**Pros:**
- Better recall (finds more relevant results)
- Handles vocabulary mismatch
- Understands synonyms and related concepts
- More comprehensive answers

**Cons:**
- Slower (multiple embedding calls)
- More expensive (uses more API calls)
- Slightly more complex

**When to use:** Use when Naive RAG misses relevant information or when you need deeper, more contextual answers.

---

### 3. ?? HyDE RAG (Advanced)

**How it works:**
```
Query ? Generate Hypothetical Answer ? Embed Answer ? 
Find Similar Chunks ? Generate Real Answer
```

**Algorithm:**
1. Generate a hypothetical answer to your question (WITHOUT looking at documents)
2. Embed the hypothetical answer
3. Search documents using the hypothetical answer embedding
4. Generate the actual answer using retrieved context

**The Magic:** Instead of searching with your question, we search with what the answer *might* look like. This bridges the gap between "query space" (how questions are asked) and "document space" (how information is written).

**Best for:**
- ? Questions where answer style differs from query style
- ? Abstract or conceptual questions
- ? Technical documentation searches
- ? When you need expert-level answers

**Example scenarios:**
- "How do I optimize performance?" 
  - Your question is short, but the answer in docs might be a detailed explanation
- "What's the best practice for X?"
  - Docs might not say "best practice" but describe the approach
- "Why does this feature exist?"
  - Docs explain the feature without explicitly stating "why"

**Pros:**
- Best semantic alignment
- Bridges question-answer style gap
- Excellent for technical/abstract queries
- Often finds non-obvious relevant content

**Cons:**
- Slowest (two LLM calls: hypothesis + answer)
- Most expensive
- Can be unpredictable if hypothesis is off-target

**When to use:** Use when other strategies fail, or when you need the most sophisticated retrieval for complex/abstract questions.

---

## Strategy Comparison Table

| Feature | Naive | Semantic | HyDE |
|---------|-------|----------|------|
| **Speed** | ??? Fast | ?? Medium | ? Slow |
| **Cost** | ?? Low | ???? Medium | ?????? High |
| **Accuracy** | ??? Good | ???? Better | ????? Best |
| **API Calls** | 1 | 3-4 | 2-3 |
| **Best For** | Facts | Context | Abstract |

---

## Real-World Usage Examples

### Scenario 1: Product Documentation
**Document:** Technical API documentation

**Question:** "How do I authenticate?"

- **Naive ?** ? Works well, likely finds "authentication" section directly
- **Semantic** ? Overkill, but would also find "security", "credentials", "access" sections
- **HyDE** ? Overkill, generates hypothesis about auth flows

**Recommendation:** Use **Naive**

---

### Scenario 2: Meeting Notes
**Document:** Informal meeting notes with varied terminology

**Question:** "What did we decide about the launch timeline?"

- **Naive** ? Might miss if notes say "deployment schedule" instead of "launch timeline"
- **Semantic ?** ? Expands to "launch", "deployment", "schedule", "timeline" - finds all related mentions
- **HyDE** ? Could work but more expensive

**Recommendation:** Use **Semantic**

---

### Scenario 3: Research Paper
**Document:** Academic paper with complex concepts

**Question:** "What are the implications of this methodology?"

- **Naive** ? Searches for "implications" and "methodology" - might miss context
- **Semantic** ? Better, expands to related terms
- **HyDE ?** ? Generates hypothesis about typical implications, finds sections discussing outcomes, results, analysis

**Recommendation:** Use **HyDE**

---

## Performance Optimization Tips

### File Size Matters
- **Max size:** 100 KB per file
- **Why:** Larger files ? more chunks ? slower embedding & retrieval
- **Tip:** Split large documents into logical sections

### Chunking Strategy
Your files are automatically split into **512-character chunks** with **50-character overlap**.

**Why overlap?** Ensures context isn't lost at chunk boundaries.

**Example:**
```
Chunk 1: "The API supports JSON and XML. The authentication uses..."
          ? (overlap)
Chunk 2: "...authentication uses OAuth 2.0. To get started..."
```

### Session Management
- **TTL:** 15 minutes of inactivity
- **Max docs:** 2 documents per session
- **Why:** Keeps the demo responsive and prevents resource exhaustion

---

## Behind the Scenes: Technical Details

### Embeddings
- **Model:** Gemini `text-embedding-004`
- **Dimensions:** 768-dimensional vectors
- **Caching:** Identical text chunks are cached to avoid redundant API calls

### Vector Search
- **Algorithm:** SIMD-accelerated cosine similarity
- **SIMD:** Single Instruction Multiple Data - processes 4-8 floats simultaneously
- **Speed:** ~10-100x faster than naive implementation
- **Top-K:** Returns 5 most relevant chunks by default

### Parallel Processing
- **Chunking:** Parallel text processing
- **Embedding:** Up to 5 concurrent API calls (rate-limited)
- **Pipeline:** Producer-consumer pattern using `Channel<T>`

---

## Tips for Best Results

### 1. Upload Quality Content
- ? Clear, well-structured text
- ? Markdown formatting preserved
- ? Avoid scanned images (text only)
- ? Don't upload extremely repetitive content

### 2. Ask Clear Questions
- ? "What are the pricing tiers?"
- ? "stuff about money"
- ? "How do I configure authentication?"
- ? "auth"

### 3. Match Strategy to Question Type
- **Factual/Lookup** ? Naive
- **Conceptual/Complex** ? Semantic
- **Abstract/Technical** ? HyDE

### 4. Iterate if Needed
- Try Naive first
- If answer is incomplete, try Semantic
- For complex topics, try HyDE

---

## Understanding the Metrics

After each query, you'll see performance metrics:

- **Retrieval Time:** How long it took to find relevant chunks
- **Generation Time:** How long AI took to generate the answer
- **Chunks Used:** Number of document chunks in the context
- **Total Time:** End-to-end processing time

**What's good?**
- Retrieval: < 500ms
- Generation: 1-3 seconds
- Total: < 5 seconds

**Slow? Check:**
- Too many documents uploaded
- Very large files
- Using HyDE (naturally slower)

---

## Troubleshooting

### "No relevant information found"
- **Try:** Different RAG strategy
- **Check:** Query phrasing
- **Verify:** Information actually exists in documents

### "Response seems off-topic"
- **Try:** More specific question
- **Switch:** From Naive to Semantic
- **Check:** Document quality and relevance

### "Slow performance"
- **Reduce:** Number of documents
- **Use:** Smaller files
- **Try:** Naive instead of HyDE

---

## What's Next?

Want to learn more about the implementation?
- Check `RAG-ARCHITECTURE.md` for technical details
- Review unit tests in `tests/MyPortfolio.Core.Tests/Features/RAG/`
- Explore the source code in `src/MyPortfolio.Core/Features/RAG/`

---

**Have questions?** The implementation showcases advanced .NET concepts:
- SIMD optimization with `Vector<T>`
- Producer-consumer pipelines with `Channel<T>`
- Memory caching with `IMemoryCache`
- Parallel processing with `Task.WhenAll`
- SSE streaming for real-time updates
