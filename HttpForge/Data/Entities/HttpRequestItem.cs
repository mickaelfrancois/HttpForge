namespace HttpForge.Data.Entities;

public enum HttpMethodKind { GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS }

public enum BodyKind { None, Json, Raw, FormUrlEncoded }

public class HttpRequestItem
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }

    public string Name { get; set; } = "Untitled";
    public HttpMethodKind Method { get; set; } = HttpMethodKind.GET;
    public string Url { get; set; } = string.Empty;

    public BodyKind BodyKind { get; set; } = BodyKind.None;
    public string? BodyContent { get; set; }
    public string? PostScript { get; set; }

    public List<HeaderItem> Headers { get; set; } = new();
    public List<QueryParamItem> QueryParams { get; set; } = new();
    public List<FormFieldItem> FormFields { get; set; } = new();
    public List<RequestVariable> Variables { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
