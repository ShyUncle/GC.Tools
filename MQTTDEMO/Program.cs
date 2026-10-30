using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using System.Buffers;
using System.Text;
using System.Threading.Channels;

namespace MQTTDEMO
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using var loggerFactory = LoggerFactory.Create(b =>
                   b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
                    .SetMinimumLevel(LogLevel.Information));
            var log = loggerFactory.CreateLogger("Lab");
            var client = new ReliableMqttClient("127.0.0.1", 1883, "gw_prod_parkA_001", log);
            await client.StartAsync();
            await Task.Delay(500);                                   // 等连接建立
            await client.SubscribeAsync("dev/test", MqttQualityOfServiceLevel.AtLeastOnce);
            client.ApplicationMessageReceived += Client_ApplicationMessageReceived;
            // ---------- 3) 业务循环 ----------
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            await StartConsumer("后台1", cts.Token);
            await Task.Delay(1000);
            await StartConsumer("后台2", cts.Token);
            int seq = 0;
            try
            {
                //模拟发送消息
                while (!cts.IsCancellationRequested)
                {
                    //Console.WriteLine("请输入模拟设备消息");
                    //var input = Console.ReadLine();
                    //await client.PublishAsync(new MQTTnet.MqttApplicationMessage
                    //{
                    //    Topic = "dev/test",
                    //    Payload = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(input)),
                    //    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce,
                    //    Retain = false,
                    //});
                    await client.PublishAsync(new MQTTnet.MqttApplicationMessage
                    {
                        Topic = "dev/test",
                        Payload = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(Random.Shared.Next(2, 100).ToString())),
                        QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce,
                        Retain = false,

                    });
                    await Task.Delay(1000);
                }
            }
            catch (OperationCanceledException) { }

            // ---------- 4) 收尾 ----------
            await client.DisposeAsync();
            log.LogInformation("已退出");
        }
        private static readonly Channel<MqttApplicationMessageReceivedEventArgs> _inbox =
    Channel.CreateBounded<MqttApplicationMessageReceivedEventArgs>(
        new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
        //作为上位机处理消息
        private static async Task Client_ApplicationMessageReceived(MQTTnet.MqttApplicationMessageReceivedEventArgs arg)
        {
            var msg = arg.ApplicationMessage;
            Console.WriteLine($"收到消息{Encoding.UTF8.GetString(msg.Payload.ToArray())}");
            _inbox.Writer.TryWrite(arg);
        }

        private static async Task StartConsumer(string name, CancellationToken cts)
        {
            // 后台消费
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            if (_inbox.Reader.TryRead(out var arg))
                            {
                                var msg = arg.ApplicationMessage;
                                var topic = msg.Topic;
                                var payload = Encoding.UTF8.GetString(msg.Payload.ToArray());
                                await Task.Delay(2000);
                                await arg.AcknowledgeAsync(cts);
                                Console.WriteLine($"{payload}消息已处理,处理器：{name}");
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.ToString());
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("出错了");
                }
            });
        }
    }
}
