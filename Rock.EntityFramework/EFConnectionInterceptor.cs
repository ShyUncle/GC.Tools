using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Rock.EntityFramework
{
    public class EFConnectionInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
        }
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            base.ConnectionOpened(connection, eventData);
        }
    }
}