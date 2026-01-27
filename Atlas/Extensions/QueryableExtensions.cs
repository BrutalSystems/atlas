using System.Linq.Expressions;
using Atlas.Helpers;
using SQLitePCL;

namespace Atlas.Extensions;

public static class QueryableExtensions
{
    public static IOrderedQueryable<T> OrderByColumn<T>(this IQueryable<T> source, string? columnPath, bool isSqlite = false)
        => source.OrderByColumnUsing(columnPath, "OrderBy", isSqlite);

    public static IOrderedQueryable<T> OrderByColumnDescending<T>(this IQueryable<T> source, string? columnPath, bool isSqlite = false)
        => source.OrderByColumnUsing(columnPath, "OrderByDescending", isSqlite);

    public static IOrderedQueryable<T> ThenByColumn<T>(this IOrderedQueryable<T> source, string? columnPath, bool isSqlite = false)
        => source.OrderByColumnUsing(columnPath, "ThenBy", isSqlite);

    public static IOrderedQueryable<T> ThenByColumnDescending<T>(this IOrderedQueryable<T> source, string? columnPath, bool isSqlite = false)
        => source.OrderByColumnUsing(columnPath, "ThenByDescending", isSqlite);

    private static IOrderedQueryable<T> OrderByColumnUsing<T>(this IQueryable<T> source, string? columnPath, string method, bool isSqlite)
    {
        if (isSqlite)
        {
            return source.OrderByColumnUsingToStringIfDateTimeOffset(columnPath, method);
        }
        else
        {
            return source.OrderByColumnUsing(columnPath, method);
        }
    }
    
    private static IOrderedQueryable<T> OrderByColumnUsing<T>(this IQueryable<T> source, string? columnPath, string method)
    {
        if (string.IsNullOrEmpty(columnPath))
            throw new ArgumentException("Column path cannot be null or empty", nameof(columnPath));

        try
        {
            var parameter = Expression.Parameter(typeof(T), "item");
            var member = columnPath.Split('.').Aggregate((Expression)parameter, Expression.PropertyOrField);
            var keySelector = Expression.Lambda(member, parameter);
            var methodCall = Expression.Call(typeof(Queryable), method, new[] { parameter.Type, member.Type }, source.Expression, Expression.Quote(keySelector));

            return (IOrderedQueryable<T>)source.Provider.CreateQuery(methodCall);
        }
        catch
        {
            var logger = Logging.CreateLogger<T>();
            logger.LogError("Failed to order by column {ColumnPath}", columnPath);
            throw;
        }
    }

    private static IOrderedQueryable<T> OrderByColumnUsingToStringIfDateTimeOffset<T>(this IQueryable<T> source, string? columnPath, string method)
    {
        if (string.IsNullOrEmpty(columnPath))
            throw new ArgumentException("Column path cannot be null or empty", nameof(columnPath));

        try
        {
            var parameter = Expression.Parameter(typeof(T), "item");
            var member = columnPath.Split('.').Aggregate((Expression)parameter, Expression.PropertyOrField);

            Expression keySelectorBody = member;
            if (IsDateTimeOffsetOrNullableDateTimeOffset(member.Type))
            {
                var toStringMethod = member.Type.GetMethod(nameof(ToString), Type.EmptyTypes);
                if (toStringMethod != null)
                {
                    keySelectorBody = Expression.Call(member, toStringMethod);
                }
            }

            var keySelector = Expression.Lambda(keySelectorBody, parameter);
            var methodCall = Expression.Call(typeof(Queryable), method, new[] { parameter.Type, keySelectorBody.Type }, source.Expression, Expression.Quote(keySelector));

            return (IOrderedQueryable<T>)source.Provider.CreateQuery(methodCall);
        }
        catch
        {
            var logger = Logging.CreateLogger<T>();
            logger.LogError("Failed to order by column {ColumnPath} using ToString() method", columnPath);
            throw;
        }
    }

    private static bool IsDateTimeOffsetOrNullableDateTimeOffset(Type type)
    {
        if (type == typeof(DateTimeOffset))
            return true;

        if (type == typeof(DateTimeOffset?))
            return true;

        if (!type.IsGenericType)
            return false;

        if (type.GetGenericTypeDefinition() != typeof(Nullable<>))
            return false;

        var inner = Nullable.GetUnderlyingType(type);
        return inner == typeof(DateTimeOffset);
    }
}