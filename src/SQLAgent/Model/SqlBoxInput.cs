namespace SQLAgent.Model;

public class SqlBoxInput
{
    public required string Message { get; set; }

    public required string ConnectionId { get; set; }

    public bool AllowWrite { get; set; }
}