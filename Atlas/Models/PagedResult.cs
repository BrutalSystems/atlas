namespace Atlas.Models;

/// <summary>
/// Represents a paginated result set with metadata about the pagination.
/// </summary>
/// <typeparam name="T">The type of items in the result set</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// The items in the current page.
    /// </summary>
    public List<T> Items { get; set; }
    
    /// <summary>
    /// Total number of items across all pages.
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int PageNumber { get; set; }
    
    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; set; }
    
    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    
    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPrevious => PageNumber > 1;
    
    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNext => PageNumber < TotalPages;
    
    /// <summary>
    /// Index of the first item on the current page (0-based).
    /// </summary>
    public int FirstItemIndex => (PageNumber - 1) * PageSize;
    
    /// <summary>
    /// Index of the last item on the current page (0-based).
    /// </summary>
    public int LastItemIndex => Math.Min(FirstItemIndex + PageSize - 1, TotalCount - 1);

    /// <summary>
    /// Initializes a new instance of the PagedResult class.
    /// </summary>
    /// <param name="items">The items in the current page</param>
    /// <param name="totalCount">Total number of items across all pages</param>
    /// <param name="pageNumber">Current page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    public PagedResult(List<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items ?? new List<T>();
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
    
    /// <summary>
    /// Creates an empty paged result.
    /// </summary>
    /// <param name="pageNumber">Current page number</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <returns>Empty paged result</returns>
    public static PagedResult<T> Empty(int pageNumber = 1, int pageSize = 50)
    {
        return new PagedResult<T>(new List<T>(), 0, pageNumber, pageSize);
    }
}