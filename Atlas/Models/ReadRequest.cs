namespace Atlas.Models;

public class QueryRequest
{
    public IQueryable? Query { get; set; } // removed from swagger w/ SwaggerOperationFilter
}

public class ReadFilter 
{
    public string? PropertyName { get; set; }
    public ReadFilterOperator? Operation { get; set; }
    public string? Value { get; set; }
    public List<ReadFilter>? Conditions { get; set; }
}

public class ReadSort 
{
    public string? ColId { get; set;}
    public string? Sort { get; set;}
}

public class ReadRequest : QueryRequest
{
    public int? Skip { get; set; }
    public int? Take { get; set; }
    public List<ReadFilter>? Filters { get; set;}
    public List<ReadSort>? Sorts { get; set;}    
}

public class ReadResponse<T> 
{
    public IEnumerable<T> Data { get; set; } = [];
    public long? LastRow { get; set; }
    public object? AdditionalData { get; set; } 
}

public enum ReadFilterOperator 
{
    Equals,
    NotEqual,
    Lt,
    LessThan,
    LtOrEq,
    Gt,
    GreaterThan,
    GtOrEq,
    NotContains,
    Contains,
    ContainsCaseSensitive,
    StartsWith,
    EndsWith,
    Blank,
    NotBlank,
    Or,
    And
}

public class InvalidFilterCriteriaException : Exception
{
    public InvalidFilterCriteriaException() : base("Invalid filter criteria") { }
    public InvalidFilterCriteriaException(string message) : base(message) { }
    public InvalidFilterCriteriaException(string message, Exception innerException) : base(message, innerException) { }
}