using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Making.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using SQLBox.Entities;
using SQLBox.Facade;
using SQLBox.Hosting.Dto;
using SQLBox.Infrastructure;

namespace SQLBox.Hosting.Services;

[MiniApi(Route = "/api/chat")]
public class ChatService(IDatabaseConnectionManager connectionManager)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// SSE流式对话接口
    /// </summary>
    [HttpPost("completion")]
    public async Task CompletionAsync(HttpContext context, CompletionInput input)
    {
        var sw = Stopwatch.StartNew();

        // 设置SSE响应头
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            // 验证连接
            var connection = await connectionManager.GetConnectionAsync(input.ConnectionId);
            if (connection == null)
            {
                await SendErrorAsync(context, "CONNECTION_NOT_FOUND",
                    $"Connection '{input.ConnectionId}' not found");
                return;
            }

            if (!connection.IsEnabled)
            {
                await SendErrorAsync(context, "CONNECTION_DISABLED",
                    $"Connection '{connection.Name}' is disabled");
                return;
            }

            // 发送开始消息
            await SendTextAsync(context, $"正在分析问题: {input.Question}");

            // 创建查询选项
            var options = new AskOptions(
                ConnectionId: input.ConnectionId,
                Dialect: input.Dialect ?? connection.DatabaseType,
                Execute: input.Execute,
                TopK: 8,
                ReturnExplanation: true,
                AllowWrite: false
            );

            // 执行查询
            await SendTextAsync(context, "正在生成SQL...");

            var result = await SqlGen.AskAsync(input.Question, options);

            // 发送SQL
            await SendSqlAsync(context, new SqlMessage
            {
                Sql = result.Sql,
                Tables = result.TouchedTables,
                Dialect = result.Dialect
            });

            if (result.Warnings != null && result.Warnings.Length > 0)
            {
                await SendTextAsync(context, $"警告: {string.Join(", ", result.Warnings)}");
            }

            // 发送解释
            if (!string.IsNullOrEmpty(result.Explanation))
            {
                await SendTextAsync(context, result.Explanation);
            }

            // TODO: 如果执行了查询，这里需要实际执行SQL并返回数据
            // 目前 SQLBox 的 SqlResult 不包含数据，需要单独执行
            if (input.Execute)
            {
                await SendTextAsync(context, "SQL 已生成，实际执行功能待实现");
            }

            sw.Stop();

            // 发送完成消息
            await SendDoneAsync(context, new DoneMessage
            {
                ElapsedMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            await SendErrorAsync(context, "EXECUTION_ERROR", ex.Message, ex.ToString());
        }
    }

    private static async Task SendTextAsync(HttpContext context, string content)
    {
        var message = new TextMessage { Content = content };
        await SendMessageAsync(context, message);
    }

    private static async Task SendSqlAsync(HttpContext context, SqlMessage message)
    {
        await SendMessageAsync(context, message);
    }

    private static async Task SendDataAsync(HttpContext context, DataMessage message)
    {
        await SendMessageAsync(context, message);
    }

    private static async Task SendChartAsync(HttpContext context, ChartMessage message)
    {
        await SendMessageAsync(context, message);
    }

    private static async Task SendErrorAsync(HttpContext context, string code, string message, string? details = null)
    {
        var errorMessage = new ErrorMessage
        {
            Code = code,
            Message = message,
            Details = details
        };
        await SendMessageAsync(context, errorMessage);
    }

    private static async Task SendDoneAsync(HttpContext context, DoneMessage message)
    {
        await SendMessageAsync(context, message);
    }

    private static async Task SendMessageAsync(HttpContext context, SSEMessage message)
    {
        var json = JsonSerializer.Serialize(message, message.GetType(), JsonOptions);
        var data = $"data: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(data);

        await context.Response.Body.WriteAsync(bytes);
        await context.Response.Body.FlushAsync();
    }
}