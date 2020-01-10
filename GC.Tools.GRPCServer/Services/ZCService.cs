using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace GC.Tools.GRPCServer
{
    public class ZCService : ZCSer.ZCSerBase
    {
        private readonly ILogger<ZCService> _logger;
        public ZCService(ILogger<ZCService> logger)
        {
            _logger = logger;
        }

        public override Task<ZCReply> SayHello(ZCRequest request, ServerCallContext context)
        {
            return Task.FromResult(new ZCReply
            {
                Message = "Hello " + request.Name
            });
        }
    }
}
