using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DnsHiJacking
{
    public sealed class DnsInterceptHostedService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await new DnsInterceptor().InterceptAsync(stoppingToken);
        }
    }
}
