# Contacts and chat RAG

A forensic chat analysis tool built on a Retrieval-Augmented Generation (RAG) pipeline.
It ingests structured phone book and chat session data, embeds them into a vector store, and answers natural-language questions grounded in the evidence.

## Architecture

**Two-layer design:**
- **Generic RAG** (`Services/`, `Interfaces/`, `Models/`) -- reusable embedding, vector search, and streaming answer generation
- **Domain layer** (`Domain/`) -- forensic-specific logic: participant resolution, aggregate queries, temporal filtering, pre-computed forensic facts

## Design Patterns

- **Content-addressable caching** -- SHA-256(filename + content) keys; skips OpenAI embedding calls when files haven't changed
- **HyDE (Hypothetical Document Embeddings)** -- generates a draft answer before embedding for better semantic retrieval
- **Decorator pattern** -- `DomainRagOrchestrator` wraps `RagOrchestrator` with domain enrichment
- **Adapter pattern** -- `OpenAiEmbeddingAdapter` bridges OpenAI SDK to the generic `IEmbeddingGenerator` interface
- **Polly resilience** -- exponential backoff with jitter for transient API failures (429, 5xx)
- **Atomic file writes** -- cache writes go to `.tmp` then `File.Move` to prevent corruption

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- OpenAI API key with access to chat and embedding models

## Setup

1. Clone the repository
2. Create a `.env` file in the project root:
   ```
   OPENAI_API_KEY=sk-...
   ```
3. Place forensic data files:
   - `phonebook.txt` -- raw phone book export
   - `chats/` -- raw chat export directory

## Usage

**Interactive mode:**
```bash
dotnet run
```

Commands in interactive mode:
| Command | Description |
|---------|-------------|
| `exit` / `quit` | Close the application |
| `exit with save` | Save conversation history as JSON and exit |
| `/clear` | Reset conversation context |
| `/help` | Show help |
| `Ctrl+C` | Cancel the current query |

**Running tests:**
```bash
dotnet test                                          # all tests
dotnet test --filter "FullyQualifiedName~Unit"       # unit only
dotnet test --filter "FullyQualifiedName~Functional" # functional only
dotnet test --verbosity normal                       # verbose output
```

**Validation test suite:**
```bash
dotnet run -- --test
```

Runs 28 automated test cases covering:
- Phone book queries (contacts, phone numbers, chat file IDs)
- Temporal filtering (monthly activity, busiest day)
- Aggregate queries (all contacts in a period, cross-file counts)
- Comparison and exclusion queries
- Miss attribution detection
- Single-chat-max and timeline edge cases

## Improvement TODO:

1. Linux
2. Page Index
3. GraphRAG
4. RAPTOR
5. MCP (drop interactive CLI)
6. C.R.U.D data for RAG data
7. Mutation tests
8. Line coverage minimum 95%, mutation coverage minimum 95%
9. Dockerfile
