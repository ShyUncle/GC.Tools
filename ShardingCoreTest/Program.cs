using ShardingCore;
using ShardingCoreTest.DomainModel;
using Microsoft.EntityFrameworkCore;
using System.Text;
namespace ShardingCoreTest
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            // Add services to the container.
            builder.Services.AddRazorPages();
            //∂ÓÕ‚ÃÌº”∑÷∆¨≈‰÷√
            builder.Services.AddShardingDbContext<MyDbContext>()
                .UseRouteConfig(op =>
                {
                    op.AddShardingTableRoute<OrderVirtualTableRoute>();
                    op.AddShardingTableRoute<SystemUserTableRoute>();
                }).UseConfig(op =>
                 {
                     op.UseShardingQuery((connStr, builder) =>
                     {
                         //connStr is delegate input param
                         builder.UseSqlServer(connStr);
                     });
                     op.UseShardingTransaction((connection, builder) =>
                     {
                         //connection is delegate input param
                         builder.UseSqlServer(connection);
                     });
                     //use your data base connection string
                     op.AddDefaultDataSource(Guid.NewGuid().ToString("n"),
                         "Data Source=localhost;Initial Catalog=ShardingCoreDB;TrustServerCertificate=True;user id=sa;password=123456;");
                     op.AddExtraDataSource(x =>
                     {
                         return new Dictionary<string, string>()
                         {
                             {"n",   "Data Source=localhost;Initial Catalog=ShardingCoreDB;TrustServerCertificate=True;user id=sa;password=123456;"
                             } ,
                              {"n1",   "Data Source=localhost;Initial Catalog=ShardingCoreDB;TrustServerCertificate=True;user id=sa;password=123456;"
                             }
                         };
                     });
                     op.AddReadWriteSeparation(o =>
                     {
                         return new Dictionary<string, IEnumerable<string>>()
                         {
                             {"n",new HashSet<string>
                             {
                                  "Data Source=localhost;Initial Catalog=ShardingCoreDB;TrustServerCertificate=True;user id=sa;password=123456;",
                                   "Data Source=localhost;Initial Catalog=ShardingCoreDB;TrustServerCertificate=True;user id=sa;password=123456;"
                             } }
                         };
                     }, ShardingCore.Sharding.ReadWriteConfigurations.ReadStrategyEnum.Loop, ShardingCore.Sharding.ReadWriteConfigurations.ReadWriteDefaultEnableBehavior.DefaultEnable);
                 }).AddShardingCore();
            builder.Services.AddControllers();
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.Services.UseAutoTryCompensateTable();
            app.UseRouting();

            app.UseAuthorization();

            app.MapRazorPages();
            app.MapControllers();
            app.Run();
        }
    }
}
