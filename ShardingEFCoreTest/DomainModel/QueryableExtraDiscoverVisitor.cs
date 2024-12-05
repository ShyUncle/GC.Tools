using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace ShardingEFCoreTest.DomainModel
{
    internal class QueryableExtraDiscoverVisitor : ExpressionVisitor
    {

        private PaginationContext _paginationContext = new PaginationContext();
        public OrderByContext _orderByContext = new OrderByContext();

        public QueryableExtraDiscoverVisitor()
        {
        }




        public PaginationContext GetPaginationContext()
        {

            return _paginationContext;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var method = node.Method;
            if (node.Method.Name == nameof(Queryable.Skip))
            {
                if (_paginationContext.HasSkip())
                    throw new Exception("more than one skip found");
                var skip = (int)(node.Arguments[1] as ConstantExpression).Value;
                _paginationContext.AddSkip(skip);
            }
            else if (node.Method.Name == nameof(Queryable.Take))
            {
                if (_paginationContext.HasTake())
                    throw new Exception("more than one take found");
                var take = (int)(node.Arguments[1] as ConstantExpression).Value;
                _paginationContext.AddTake(take);
            }
            else if (method.Name == nameof(Queryable.OrderBy) || method.Name == nameof(Queryable.OrderByDescending) || method.Name == nameof(Queryable.ThenBy) || method.Name == nameof(Queryable.ThenByDescending))
            {
                MemberExpression expression = null;
                var orderbody = ((node.Arguments[1] as UnaryExpression).Operand as LambdaExpression).Body;
                if (orderbody is MemberExpression orderMemberExpression)
                {
                    expression = orderMemberExpression;
                }
                else if (orderbody.NodeType == ExpressionType.Convert && orderbody is UnaryExpression orderUnaryExpression)
                {
                    if (orderUnaryExpression.Operand is MemberExpression orderMemberConvertExpression)
                    {
                        expression = orderMemberConvertExpression;
                    }
                }
                List<string> properties = new List<string>();
                if (expression != null)
                {
                    GetPropertyInfo(properties, expression);
                }

                _orderByContext.OrderBys.Insert(0, new OrderByContext.OrderByProperty
                {
                    IsAsc = method.Name == nameof(Queryable.OrderBy) || method.Name == nameof(Queryable.ThenBy),
                    PropertyName = properties.FirstOrDefault()
                });
            }
            return base.VisitMethodCall(node);
        }
        private void GetPropertyInfo(List<string> properties, MemberExpression memberExpression)
        {
            properties.Add(memberExpression.Member.Name);
            if (memberExpression.Expression is MemberExpression member)
            {
                GetPropertyInfo(properties, member);
            }
        }


    }
    public sealed class OrderByContext
    {
        public List<OrderByProperty> OrderBys { get; private set; } = new List<OrderByProperty>();

        public void Add(OrderByProperty orderBy)
        {
            OrderBys.Add(orderBy);
        }
        public class OrderByProperty
        {
            public string PropertyName { get; set; }
            public bool IsAsc { get; set; }
        }
    }
    public sealed class PaginationContext
    {
        public int? Skip { get; private set; }
        public int? Take { get; private set; }


        public bool HasSkip()
        {
            return Skip.HasValue;
        }

        public bool HasTake()
        {
            return Take.HasValue;
        }

        public void AddSkip(int skip)
        {
            if (Skip.HasValue)
            {
                throw new Exception("multi skip");
            }

            Skip = skip;
        }

        public void AddTake(int take)
        {
            if (Take.HasValue)
            {
                throw new Exception("multi take");
            }

            Take = take;
        }

        /// <summary>
        /// 替换为固定的take一般用于first 1 single 2 last 1
        /// </summary>
        /// <param name="take"></param>
        public void ReplaceToFixedTake(int take)
        {
            Take = take;
        }

        public override string ToString()
        {
            return $"{nameof(Skip)}: {Skip},  {nameof(Take)}: {Take}";
        }
    }
    internal class RemoveSkipAndTakeVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(Queryable.Skip))
                return base.Visit(node.Arguments[0]);
            if (node.Method.Name == nameof(Queryable.Take))
                return base.Visit(node.Arguments[0]);

            return base.VisitMethodCall(node);
        }
    }
    /// <summary>
    /// 删除Skip表达式
    /// </summary>
    internal class RemoveSkipVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(Queryable.Skip))
                return base.Visit(node.Arguments[0]);

            return base.VisitMethodCall(node);
        }
    }
    internal class RemoveTakeVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(Queryable.Take))
                return base.Visit(node.Arguments[0]);

            return base.VisitMethodCall(node);
        }
    }
    internal class RemoveOrderByVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(Queryable.OrderBy))
                return base.Visit(node.Arguments[0]);

            return base.VisitMethodCall(node);
        }
    }
    internal static class IShardingQueryableExtension
    {
        private static readonly MethodInfo QueryableSkipMethod = typeof(Queryable).GetMethod(nameof(Queryable.Skip));
        private static readonly MethodInfo QueryableTakeMethod = typeof(Queryable).GetMethods().First(
            m => m.Name == nameof(Queryable.Take)
                 && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(int));

        internal static IQueryable RemoveSkipAndTake(this IQueryable source)
        {
            var expression = new RemoveSkipAndTakeVisitor().Visit(source.Expression);
            return source.Provider.CreateQuery(expression);
        }
        /// <summary>
        /// 删除Skip表达式
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="source">数据源</param>
        /// <returns></returns>
        internal static IQueryable RemoveSkip(this IQueryable source)
        {
            var expression = new RemoveSkipVisitor().Visit(source.Expression);
            return source.Provider.CreateQuery(expression);
        }

        internal static IQueryable ReSkip(this IQueryable source, int skip)
        {
            MethodInfo method = QueryableSkipMethod.MakeGenericMethod(source.ElementType);
            var expression = Expression.Call(
                method,
                source.Expression,
                Expression.Constant(skip));
            return source.Provider.CreateQuery(expression);
        }

        internal static IQueryable ReTake(this IQueryable source, int take)
        {
            MethodInfo method = QueryableTakeMethod.MakeGenericMethod(source.ElementType);
            var expression = Expression.Call(
                method,
                source.Expression,
                Expression.Constant(take));
            return source.Provider.CreateQuery(expression);
        }
        /// <summary>
        /// 删除Take表达式
        /// </summary>
        /// <param name="source">数据源</param>
        /// <returns></returns>
        internal static IQueryable RemoveTake(this IQueryable source)
        {
            var expression = new RemoveTakeVisitor().Visit(source.Expression);
            return source.Provider.CreateQuery(expression);
        }
        [ExcludeFromCodeCoverage]
        internal static IQueryable RemoveOrderBy(this IQueryable source)
        {
            var expression = new RemoveOrderByVisitor().Visit(source.Expression);
            return source.Provider.CreateQuery(expression);
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
        internal static IQueryable<TSource> ReplaceDbContextQueryableWithType<TSource>(this IQueryable<TSource> source, DbContext dbContext)
        {
            DbContextReplaceQueryableVisitor replaceQueryableVisitor = new DbContextReplaceQueryableVisitor(dbContext);
            var newExpression = replaceQueryableVisitor.Visit(source.Expression);
            return (IQueryable<TSource>)replaceQueryableVisitor.Source.Provider.CreateQuery(newExpression);
        }
        internal static Expression ReplaceDbContextExpression(this Expression queryExpression, DbContext dbContext)
        {
            DbContextReplaceQueryableVisitor replaceQueryableVisitor = new DbContextReplaceQueryableVisitor(dbContext);
            var expression = replaceQueryableVisitor.Visit(queryExpression);
            return expression;
        }
    }
}
