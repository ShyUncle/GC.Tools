using ShardingCore.Core.EntityMetadatas;
using ShardingCore.VirtualRoutes.Mods;
using ShardingCore.VirtualRoutes.Months;
using System.Text;

namespace ShardingCoreTest.DomainModel
{
    public class OrderVirtualTableRoute : AbstractSimpleShardingMonthKeyDateTimeVirtualTableRoute<Order>
    {
        /// <summary>
        /// fixed value don't use DateTime.Now because if  if application restart this value where change
        /// </summary>
        /// <returns></returns>
        public override DateTime GetBeginTime()
        {
            return new DateTime(2021, 1, 1);
        }
        /// <summary>
        /// configure sharding property
        /// </summary>
        /// <param name="builder"></param>

        public override void Configure(EntityMetadataTableBuilder<Order> builder)
        { 
            builder.ShardingProperty(o => o.CreationTime);
        }
        /// <summary>
        /// enable auto create table job
        /// </summary>
        /// <returns></returns>

        public override bool AutoCreateTableByTime()
        {
            return true;
        }
    }
    public class SystemUserTableRoute : AbstractSimpleShardingModKeyIntVirtualTableRoute<SystemUser>
    {
        public SystemUserTableRoute() : base(2, 5)
        {
        }

        public override void Configure(EntityMetadataTableBuilder<SystemUser> builder)
        {
            builder.ShardingProperty(o => o.Id);
        }

    }
}
