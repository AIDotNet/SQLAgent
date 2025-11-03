using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure.Defaults;

// Vector-store retriever: uses persistent ITableVectorStore to fetch top-K similar tables.
// 遵循规范命名，不使用“增强/加强”等词。
public sealed class VectorSchemaRetriever(ITableVectorStore vectorStore, bool autoUpdateVectors = true)
    : ISchemaRetriever
{
    private readonly ITableVectorStore _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));

    public async Task<SchemaContext> RetrieveAsync(string question, DatabaseSchema schema, SchemaIndex index, int topK, CancellationToken ct = default)
    {
        // 1) 可选：增量更新（首次或变更时）
        if (autoUpdateVectors)
        {
            var toUpdate = new List<TableDoc>();
            foreach (var table in schema.Tables)
            {
                var upToDate = await _vectorStore.IsTableVectorUpToDateAsync(schema.ConnectionId, table.Schema, table.Name, ct);
                if (!upToDate) toUpdate.Add(table);
            }
            if (toUpdate.Count > 0)
            {
                await _vectorStore.SaveTableVectorsBatchAsync(schema.ConnectionId, toUpdate, new Dictionary<string, string>(), ct);
            }
        }

        // 2) 调用向量存储进行相似检索
        var results = await _vectorStore.SearchSimilarTablesAsync(schema.ConnectionId, question, Math.Max(1, topK), ct);
        var tables = results.Select(r => r.Table).ToList();

        // 3) 返回上下文
        return new SchemaContext
        {
            ConnectionId = schema.ConnectionId,
            Tables = tables
        };
    }
}

