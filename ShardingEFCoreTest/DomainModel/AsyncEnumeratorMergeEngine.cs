using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace ShardingEFCoreTest.DomainModel
{
    /// <summary>
    /// 结果集合
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class AsyncEnumeratorMergeEngine<T> : IAsyncEnumerable<T>, IEnumerable<T>
    {
        private Type _entityType;
        private ShardingDbContext _context;
        private Expression _expression;
        public AsyncEnumeratorMergeEngine(Type entityType, ShardingDbContext dbContext, Expression query)
        {
            _context = dbContext;
            _entityType = entityType;
            _expression = query;
        }
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            throw new Exception();
        }

        public IEnumerator<T> GetEnumerator()
        {
            var tails = new List<string> { "00", "01", "02", "03", "04" };

            var result = new List<IEnumerator<T>>();

            QueryableExtraDiscoverVisitor visitor = new QueryableExtraDiscoverVisitor();
            visitor.Visit(_expression);
            var pageContext = visitor.GetPaginationContext();
            Type type = typeof(EnumerableQuery<>);
            type = type.MakeGenericType(_entityType);
            var queryable = (IQueryable)Activator.CreateInstance(type, _expression);
            queryable = queryable.RemoveSkipAndTake().ReSkip(0).ReTake((pageContext.Take ?? 0) + (pageContext.Skip ?? 0));
            foreach (var item in tails)
            {
                var dbContext = new DbContextFactory().Creat(item);
                {
                    _ = dbContext.Model;
                    var dbContextDependencies =
                typeof(DbContext).GetTypePropertyValue(dbContext, "DbContextDependencies") as
                    IDbContextDependencies;


                    dbContext.ShardingRule = "";
                    var newQueryable = (IQueryable<T>)queryable.ReplaceDbContextQueryable(dbContext);
                    var a = newQueryable.AsEnumerable().GetEnumerator();
                    result.Add(a);
                }
            }
            return new StreamMergeAsyncEnumerator<T>(result, pageContext.Take ?? 0, pageContext.Skip ?? 0, visitor._orderByContext);
        }

        private Expression PareseExpress()
        {


            Expression expression = _expression;
            return expression;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// 返回需要的结果迭代
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class StreamMergeAsyncEnumerator<T> : IAsyncEnumerator<T>, IEnumerator<T>
    {
        private readonly MultiOrderStreamMergeAsyncEnumerator<T> _asyncSource;
        private int Take;
        private int Skip;
        private int realSkip = 0;
        private int realTake = 0;
        int _position = -1;
        public StreamMergeAsyncEnumerator(List<IEnumerator<T>> enumerators, int take, int skip, OrderByContext orderByContext)
        {
            _asyncSource = new MultiOrderStreamMergeAsyncEnumerator<T>(enumerators, orderByContext);
            Take = take;
            Skip = skip;
        }

        public T Current => _asyncSource.Current;


        object System.Collections.IEnumerator.Current => Current;

        public void Dispose()
        {

        }

        public async ValueTask DisposeAsync()
        {
            Dispose();
        }

        public bool MoveNext()
        {
            while (Skip > this.realSkip)
            {
                var has = _asyncSource.MoveNext();
                realSkip++;
                if (!has)
                    return false;
            }

            var next = _asyncSource.MoveNext();

            if (next)
            {
                if (Take > 0)
                {
                    realTake++;
                    if (realTake > Take)
                        return false;
                }
            }

            return next;
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            return await Task.FromResult(MoveNext());
        }

        public void Reset()
        {
            _position = -1;
        }
    }
    internal class MyCompare : IComparer<string>
    {
        bool _isAsc;
        public MyCompare(bool isAsc)
        {
            _isAsc = isAsc;
        }
        public int Compare(string? x, string? y)
        {
            if (null == x && null == y)
            {
                return 0;
            }
            if (null == x)
            {
                return _isAsc ? -1 : 1;
            }
            if (null == y)
            {
                return _isAsc ? 1 : -1;
            }
            if (_isAsc)
                return x.CompareTo(y);
            else
                return y.CompareTo(x);
        }
    }
    /// <summary>
    /// 合并多个结果源
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class MultiOrderStreamMergeAsyncEnumerator<T> : IEnumerator<T>
    {
        private readonly IEnumerable<IEnumerator<T>> _enumerators;
        private IEnumerator<T> _currentEnumerator;
        private int index = 0;
        private PriorityQueue<IEnumerator<T>, string> _queue;
        private T RealValue;
        private OrderByContext _orderByContext;
        public MultiOrderStreamMergeAsyncEnumerator(List<IEnumerator<T>> enumerators, OrderByContext orderByContext)
        {
            _orderByContext = orderByContext;
            _enumerators = enumerators;
            var orderBy = orderByContext.OrderBys.FirstOrDefault() ?? new OrderByContext.OrderByProperty { };
            _queue = new PriorityQueue<IEnumerator<T>, string>(new MyCompare(orderBy.IsAsc));
            foreach (var item in enumerators)
            {
                if (item.MoveNext())
                {
                    var obj = item.Current ?? default(T);
                    var entityType = obj.GetType();
                    _queue.Enqueue(item, entityType.GetTypePropertyValue(obj, orderBy.PropertyName).ToString());
                }
            }
        }
        private string GetOrderValues(IEnumerator<T> item)
        {
            var obj = item.Current ?? default(T);
            var entityType = obj.GetType();
            return entityType.GetTypePropertyValue(obj, _orderByContext.OrderBys.FirstOrDefault().PropertyName).ToString();
        }
        public bool MoveNext()
        {

            if (_queue.Count == 0)
                return false;
            var first = _queue.Dequeue();
            RealValue = first.Current;
            if (first.MoveNext())
            {
                _queue.Enqueue(first, GetOrderValues(first));
            }
            if (_queue.Count == 0)
                return false;
            _currentEnumerator = _queue.Peek();
            return true;
        }

        public void Reset()
        {
            throw new System.NotImplementedException();
        }


        public T Current => RealValue;

        object IEnumerator.Current => RealValue;

        public void Dispose()
        {
            foreach (var enumerator in _enumerators)
            {
                enumerator.Dispose();
            }
        }
    }


}
