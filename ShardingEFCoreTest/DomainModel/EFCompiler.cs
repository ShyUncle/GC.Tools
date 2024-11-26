using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace ShardingEFCoreTest.DomainModel
{
    public class EFQueryCompiler : QueryCompiler
    {
       
        public EFQueryCompiler(IQueryContextFactory queryContextFactory, ICompiledQueryCache compiledQueryCache, ICompiledQueryCacheKeyGenerator compiledQueryCacheKeyGenerator, IDatabase database, IDiagnosticsLogger<DbLoggerCategory.Query> logger, ICurrentDbContext currentContext, IEvaluatableExpressionFilter evaluatableExpressionFilter, IModel model) : base(queryContextFactory, compiledQueryCache, compiledQueryCacheKeyGenerator, database, logger, currentContext, evaluatableExpressionFilter, model)
        {
        }
        
        public override TResult Execute<TResult>(Expression query)
        {

            var entityType = typeof(TResult).GetGenericArguments().First();
            var param = Activator.CreateInstance(typeof(EnumrableTest<>).MakeGenericType(entityType));
            var instanse = Activator.CreateInstance(typeof(List<>).MakeGenericType(entityType), param);
  
            return (TResult)instanse;
        }

        public override TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken = default)
        {
            var a = Execute<IEnumerable<SystemUser>>(null);
            return base.ExecuteAsync<TResult>(query, cancellationToken);
        }
    }
}
