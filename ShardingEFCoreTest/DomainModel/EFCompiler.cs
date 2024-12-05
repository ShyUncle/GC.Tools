using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using ShardingEFCoreTest.DomainModel;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ShardingEFCoreTest.DomainModel
{
    public class SharddingService
    {

    }
    public class EFQueryCompiler : QueryCompiler
    {
        ShardingDbContext _context;
        public EFQueryCompiler(IQueryContextFactory queryContextFactory, ICompiledQueryCache compiledQueryCache, ICompiledQueryCacheKeyGenerator compiledQueryCacheKeyGenerator, IDatabase database, IDiagnosticsLogger<DbLoggerCategory.Query> logger, ICurrentDbContext currentContext, IEvaluatableExpressionFilter evaluatableExpressionFilter, IModel model) : base(queryContextFactory, compiledQueryCache, compiledQueryCacheKeyGenerator, database, logger, currentContext, evaluatableExpressionFilter, model)
        {
            _context = (ShardingDbContext)currentContext.Context;
        }

        public override TResult Execute<TResult>(Expression query)
        {
            if (!string.IsNullOrEmpty(_context.ShardingRule))
            {
                var entityType = typeof(TResult).GetGenericArguments().First();
                var param = Activator.CreateInstance(typeof(AsyncEnumeratorMergeEngine<>).MakeGenericType(entityType), entityType, _context, query);

                return (TResult)param;

            }
            //原生查询
            return base.Execute<TResult>(query);
        }

        public override TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken = default)
        {
            var a = Execute<IEnumerable<SystemUser>>(null);
            return base.ExecuteAsync<TResult>(query, cancellationToken);
        }
    }
}



public static class Extension
{
    public static Type GetGenericType0(this Type genericType, Type arg0Type)
    {
        return genericType.MakeGenericType(arg0Type);
    }
    public static bool IsEmpty<T>(this IEnumerable<T> source)
    {
        return source == null || !source.Any();
    }
    /// <summary>
    /// 切换数据源,保留原始数据源中的Expression
    /// </summary>
    /// <param name="source">原数据源</param>
    /// <param name="dbContext">新数据源</param>
    /// <returns></returns>
    internal static IQueryable ReplaceDbContextQueryable(this IQueryable source, DbContext dbContext)
    {
        DbContextReplaceQueryableVisitor replaceQueryableVisitor = new DbContextReplaceQueryableVisitor(dbContext);
        var newExpression = replaceQueryableVisitor.Visit(source.Expression);
        return replaceQueryableVisitor.Source.Provider.CreateQuery(newExpression);
    }
    public static IQueryable<TSource> ReplaceDbContextQueryableWithType<TSource>(this IQueryable<TSource> source, DbContext dbContext)
    {
        DbContextReplaceQueryableVisitor replaceQueryableVisitor = new DbContextReplaceQueryableVisitor(dbContext);
        var newExpression = replaceQueryableVisitor.Visit(source.Expression);
        return (IQueryable<TSource>)replaceQueryableVisitor.Source.Provider.CreateQuery(newExpression);
    }
    public static bool IsMethodReturnTypeQueryableType(this Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));
        return typeof(IQueryable).IsAssignableFrom(type);
    }
    public static bool IsMemberQueryable(this MemberExpression memberExpression)
    {
        if (memberExpression == null)
            throw new ArgumentNullException(nameof(memberExpression));
        return (memberExpression.Type.FullName?.StartsWith("System.Linq.IQueryable`1") ?? false) || typeof(IQueryable).IsAssignableFrom(memberExpression.Type) || typeof(DbContext).IsAssignableFrom(memberExpression.Type);
    }
    private static readonly BindingFlags _bindingFlags
            = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static;

    /// <summary>
    /// 获取某字段值
    /// </summary>
    /// <param name="type">类型</param>
    /// <param name="obj">对象</param>
    /// <param name="propertyName">属性名</param>
    /// <returns></returns>
    public static object GetTypePropertyValue(this Type type, object obj, string propertyName)
    {
        var property = type.GetUltimateShadowingProperty(propertyName, _bindingFlags);
        if (property != null)
        {
            return property.GetValue(obj);
        }
        else
        {
            return null;
        }
    }
    /// <summary>
    /// https://github.com/nunit/nunit/blob/111fc6b5550f33b4fceb6ac8693c5692e99a5747/src/NUnitFramework/framework/Internal/Reflect.cs
    /// </summary>
    /// <param name="type"></param>
    /// <param name="name"></param>
    /// <param name="bindingFlags"></param>
    /// <returns></returns>
    public static PropertyInfo GetUltimateShadowingProperty(this Type type, string name, BindingFlags bindingFlags)
    {
        if ((bindingFlags & BindingFlags.DeclaredOnly) != 0)
        {
            // If you're asking us to search a hierarchy but only want properties declared in the given type,
            // you're in the wrong place but okay:
            return type.GetProperty(name, bindingFlags);
        }

        if ((bindingFlags & (BindingFlags.Public | BindingFlags.NonPublic)) == (BindingFlags.Public | BindingFlags.NonPublic))
        {
            // If we're searching for both public and nonpublic properties, search for only public first
            // because chances are if there is a public property, it would be very surprising to detect the private shadowing property.

            for (var publicSearchType = type; publicSearchType != null; publicSearchType = publicSearchType.GetTypeInfo().BaseType)
            {
                var property = publicSearchType.GetProperty(name, (bindingFlags | BindingFlags.DeclaredOnly) & ~BindingFlags.NonPublic);
                if (property != null) return property;
            }

            // There is no public property, so may as well not ask to include them during the second search.
            bindingFlags &= ~BindingFlags.Public;
        }

        for (var searchType = type; searchType != null; searchType = searchType.GetTypeInfo().BaseType)
        {
            var property = searchType.GetProperty(name, bindingFlags | BindingFlags.DeclaredOnly);
            if (property != null) return property;
        }

        return null;
    }

}