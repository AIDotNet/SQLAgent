using System.Text.Json;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;

namespace SQLBox.Infrastructure;

/// <summary>
/// 表向量存储记录，支持动态维度
/// Table vector storage record with dynamic dimensions support
/// </summary>
public sealed class TableVectorRecord
{
    /// <summary>
    /// 唯一标识：{connectionId}:{schema}:{tableName}:{modelName}
    /// 支持同一表的多个模型版本
    /// Unique ID: {connectionId}:{schema}:{tableName}:{modelName}
    /// Supports multiple model versions for the same table
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 数据库连接ID，用于多租户隔离
    /// Database connection ID for multi-tenancy isolation
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 表所属的架构/模式名称
    /// Schema/namespace name the table belongs to
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// 表名
    /// Table name
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// 用于生成向量的嵌入模型名称（如 "text-embedding-ada-002"）
    /// Embedding model name used to generate the vector
    /// </summary>
    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>
    /// 向量维度（如 1536, 3072 等）
    /// Vector dimensions (e.g., 1536, 3072, etc.)
    /// </summary>
    public int Dimensions { get; set; }

    /// <summary>
    /// 可搜索的文本内容（表名 + 描述 + 别名 + 列信息）
    /// Searchable text content (table name + description + aliases + column info)
    /// </summary>
    public string SearchableText { get; set; } = string.Empty;

    /// <summary>
    /// 表的完整元数据（JSON序列化的TableDoc，不包含Vector）
    /// Complete table metadata (JSON-serialized TableDoc without Vector)
    /// </summary>
    public string TableMetadata { get; set; } = string.Empty;

    /// <summary>
    /// 向量创建时间，用于版本管理和缓存失效
    /// Vector creation timestamp for version management and cache invalidation
    /// </summary>
    public long CreatedAt { get; set; }

    /// <summary>
    /// 向量数据（动态维度，不使用特性指定维度）
    /// Vector data (dynamic dimensions, no attribute-specified dimension)
    /// </summary>
    public ReadOnlyMemory<float> Vector { get; set; }

    /// <summary>
    /// 构建记录ID
    /// Build record ID
    /// </summary>
    public static string BuildId(string connectionId, string schema, string tableName, string modelName)
    {
        // 使用 : 分隔符，便于解析和调试
        return $"{connectionId}:{schema}:{tableName}:{modelName}";
    }

    /// <summary>
    /// 构建搜索文本，包含表的所有可搜索信息
    /// Build searchable text containing all searchable information of the table
    /// </summary>
    public static string BuildSearchableText(Entities.TableDoc table)
    {
        var parts = new List<string>
        {
            table.Name,
            table.Description
        };

        if (table.Aliases.Length > 0)
        {
            parts.AddRange(table.Aliases);
        }

        // 添加列名和描述
        foreach (var col in table.Columns)
        {
            parts.Add(col.Name);
            if (!string.IsNullOrWhiteSpace(col.Description))
            {
                parts.Add(col.Description);
            }
        }

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>
    /// 序列化TableDoc为JSON（不包含Vector以节省空间）
    /// Serialize TableDoc to JSON (excluding Vector to save space)
    /// </summary>
    public static string SerializeTableMetadata(Entities.TableDoc table)
    {
        // 序列化 TableDoc（不包含 Vector）以便检索时还原表结构与元数据
        var metadata = new
        {
            table.ConnectionId,
            table.Schema,
            table.Name,
            table.Aliases,
            table.Description,
            table.Columns,
            table.PrimaryKey,
            table.ForeignKeys,
            table.Stats
        };

        return JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// 反序列化JSON为TableDoc
    /// Deserialize JSON to TableDoc
    /// </summary>
    public static Entities.TableDoc DeserializeTableMetadata(string json, ReadOnlyMemory<float> vector)
    {
        var doc = JsonSerializer.Deserialize<Entities.TableDoc>(json);
        if (doc != null)
        {
            doc.Vector = vector.ToArray();
        }

        return doc ?? new Entities.TableDoc();
    }
}