namespace OrderHub.Client.Components;

/// <summary>
/// Pagination state shared between a list page and the <see cref="Pager"/> component.
/// </summary>
public class PagerState
{
    private int _page = 1;

    public int Page
    {
        get => _page;
        set => _page = Math.Max(1, value);
    }

    public int PageSize { get; set; } = 10;

    public int TotalCount { get; set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    /// <summary>Pages to render in the pager strip (centered around the current page).</summary>
    public IEnumerable<int> VisiblePages
    {
        get
        {
            const int window = 2;
            var start = Math.Max(1, Page - window);
            var end = Math.Min(TotalPages, Page + window);
            for (var i = start; i <= end; i++) yield return i;
        }
    }

    /// <summary>Resets to page 1 (e.g. after a search term change).</summary>
    public void Reset() => _page = 1;
}
