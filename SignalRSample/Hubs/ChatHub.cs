using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace SignalRSample.Hubs
{
    public class UserChatIdentifyMainInfo
    {

        /// <summary>
        /// 客户端推送消息链接Id
        /// </summary>
        public string ConnectionId { get; set; }
        /// <summary>
        /// 聊天昵称
        /// </summary>
        public string NickName { get; set; }
        /// <summary>
        /// 聊天头像
        /// </summary>
        public string HeadUrl { get; set; }
    }
    public interface IChatClient
    {
        Task ReceiveMessage(string user, string message);
        Task<string> GetMessage();
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class ChatHub : Hub<IChatClient>
    {
        private readonly IMemoryCache _memoryCache;
        public ChatHub(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        public static ConcurrentDictionary<string, UserChatIdentifyMainInfo> UserConnectionList = new ConcurrentDictionary<string, UserChatIdentifyMainInfo>();
        [LanguageFilter(FilterArgument = 1)]
        public async Task SendMessage(string user, string message)
        {
            if (!await CheckRateLimit(Context.User.Identity.Name)) return;
            await Clients.All.ReceiveMessage(user, message); 
        }
        public class RateLimitCacheEntry
        {
            public DateTime STime { get; set; }
            public int Count { get; set; }
        }

        private async Task<bool> CheckRateLimit(string id)
        {
            string cacheKey = "ratelimit" + id;
            int windowSeconds = 10;
            if (!_memoryCache.TryGetValue(cacheKey, out RateLimitCacheEntry cacheValue))
            {
                cacheValue = new RateLimitCacheEntry()
                {
                    STime = DateTime.Now,
                    Count = 1
                };

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(windowSeconds));
                _memoryCache.Set(cacheKey, cacheValue, cacheEntryOptions);
            }
            if (cacheValue.Count > 5)
            {
                await Clients.User(id).ReceiveMessage("系统消息", "发这么快干啥");
                return false;
            }
            cacheValue.Count += 1;
            _memoryCache.Set(cacheKey, cacheValue, cacheValue.STime.AddSeconds(windowSeconds));
            return true;
        }

        [HubMethodName("SendMessageToUser")]
        public async Task SendMessageToUser(string user, string message)
        {
            if (!await CheckRateLimit(Context.User.Identity.Name)) return;
            await Clients.User(user).ReceiveMessage(Context.User.Identity.Name, message);
        }
        public async Task SendMessageToCaller(string user, string message)
            => await Clients.Caller.ReceiveMessage(user, message);

        public async Task SendMessageToGroup(string user, string message)
            => await Clients.Group("SignalR Users").ReceiveMessage(user, message);

        public async Task<string> WaitForMessage(string connectionId)
        {
            string message = await Clients.Client(connectionId).GetMessage();
            return message;
        }
        public override async Task OnConnectedAsync()
        { 
            var u = Context.User;
            var s = Context.UserIdentifier;
            //每次都会产生新的ConnectionId
            string connectionId = Context.ConnectionId;
            var userInfo = new UserChatIdentifyMainInfo()
            {
                ConnectionId = connectionId,
                HeadUrl = $"/images/userHead/{(new Random()).Next(1, 100).ToString()}.jpg",
                NickName = u.Identity.Name
            };
            UserConnectionList.TryAdd(connectionId, userInfo);

            var otherUserConnections = UserConnectionList.Where(q => q.Key != connectionId).Select(q => q.Key).ToList();
            //通知其他用户有新用户上线
            if (otherUserConnections != null && otherUserConnections.Count > 0)
            {
                await SendMessage("系统消息", userInfo.NickName + "上线了");
            }

            //通知自己客户端信息
            var otherClients = UserConnectionList.Where(q => q.Key != connectionId).Select(q => q.Value).ToList();
            await SendMessageToCaller("系统消息", u.Identity.Name + "，你好");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            string connectionId = Context.ConnectionId;
            var userChatIdentifyMainInfo = new UserChatIdentifyMainInfo();
            //移除客户端链接
            if (!UserConnectionList.TryRemove(connectionId, out userChatIdentifyMainInfo))
                return;

            List<string> connectionIds = UserConnectionList.Select(q => q.Key).ToList();

            if (connectionIds != null && connectionIds.Count > 0)
            {
                await SendMessage(userChatIdentifyMainInfo.NickName, "有缘再会");
            }
            await base.OnDisconnectedAsync(exception);
        }

    }
}
