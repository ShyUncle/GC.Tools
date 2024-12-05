using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Collections.Generic;
using System.Reflection;

namespace ShardingEFCoreTest.DomainModel
{
    public class DbContextFactory
    {
        public ShardingDbContext Creat(string shardingRule)
        {
            DbContextOptionsBuilder<DbContextBase> optionsBuilder = new DbContextOptionsBuilder<DbContextBase>();
            optionsBuilder.UseSqlServer("Data Source=localhost;Initial Catalog=ShardingCoreDB;TrustServerCertificate=True;user id=sa;password=123456;");
            optionsBuilder.LogTo(Console.WriteLine);
            var options = optionsBuilder.Options;
            return new ShardingDbContext(shardingRule, options);
        }
    }

    public class DbContextBase : DbContext
    {
        public string ShardingRule { get; set; }
        public DbContextBase(string shardingRule, DbContextOptions options) : base(options)
        {
            ShardingRule = shardingRule;
        }
    }
    public class ShardingDbContext : DbContextBase
    {
        public ShardingDbContext(string shardingRule, DbContextOptions options) : base(shardingRule, options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {

            base.OnConfiguring(optionsBuilder);

            optionsBuilder.ReplaceService<IModelCacheKeyFactory, DynamicModelCacheKeyFactoryDesignTimeSupport>();
            optionsBuilder.ReplaceService<IModelCustomizer, ShardingModelCustomizer>();
            if (string.IsNullOrEmpty(ShardingRule))
                optionsBuilder.ReplaceService<IQueryCompiler, EFQueryCompiler>();
        }
    }
    public class DynamicModelCacheKeyFactoryDesignTimeSupport : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            if (context is DbContextBase dynamicContext)
            {
                return (context.GetType(), dynamicContext.ShardingRule, Guid.NewGuid().ToString("N"), designTime);
            }
            return (object)context.GetType();
        }
    }

    public class ShardingModelCustomizer : ModelCustomizer
    {

        public ShardingModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            var dbContextBase = context as DbContextBase;
            //查找需要重新映射表名的类
            var shardingTypes = new List<Type>() { typeof(SystemUser) };

            if (shardingTypes != null && shardingTypes.Count() > 0)
            {

                if (context is DbContextBase contextBase)
                {

                    foreach (var type in shardingTypes)
                    {
                        if (string.IsNullOrEmpty(contextBase.ShardingRule))
                        {
                            contextBase.ShardingRule = "00";//todo:写死
                        }
                        modelBuilder.Entity(type).ToTable($"{type.Name}_{contextBase.ShardingRule}");
                    }
                }

            }

        }

    }
}
