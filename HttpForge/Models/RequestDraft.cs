using HttpForge.Data.Entities;

namespace HttpForge.Models;

public class RequestDraft
{
    public int RequestId { get; init; }
    // Settable so a successful save can rebase the draft onto the version it just
    // wrote (see RequestSaveService.SaveResult.SavedAt); otherwise the next save by
    // the same user falsely conflicts with their own previous save.
    public DateTime LoadedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public HttpMethodKind Method { get; set; }
    public string Url { get; set; } = string.Empty;
    public BodyKind BodyKind { get; set; }
    public string? BodyContent { get; set; }
    public string? PostScript { get; set; }
    public bool IgnoreTlsErrors { get; set; }
    public List<HeaderItem> Headers { get; set; } = [];
    public List<QueryParamItem> QueryParams { get; set; } = [];
    public List<FormFieldItem> FormFields { get; set; } = [];
    public List<RequestVariable> Variables { get; set; } = [];
    public bool IsDirty { get; private set; }

    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    public static RequestDraft FromRequest(HttpRequestItem r, DateTime loadedAt) => new()
    {
        RequestId = r.Id,
        LoadedAt = loadedAt,
        Name = r.Name,
        Method = r.Method,
        Url = r.Url,
        BodyKind = r.BodyKind,
        BodyContent = r.BodyContent,
        PostScript = r.PostScript,
        IgnoreTlsErrors = r.IgnoreTlsErrors,
        Headers = r.Headers.ToList(),
        QueryParams = r.QueryParams.ToList(),
        FormFields = r.FormFields.ToList(),
        Variables = r.Variables.ToList()
    };
}
