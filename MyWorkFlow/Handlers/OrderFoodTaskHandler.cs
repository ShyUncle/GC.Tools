using Elsa.Extensions;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Notifications;

namespace MyWorkFlow.Handlers
{
    public class OrderFoodTaskHandler : INotificationHandler<RunTaskRequest>
    {
        private readonly ITaskReporter _taskReporter;

        public OrderFoodTaskHandler(ITaskReporter taskReporter)
        {
            _taskReporter = taskReporter;
        }

        public async Task HandleAsync(RunTaskRequest notification, CancellationToken cancellationToken)
        {
            if (notification.TaskName != "OrderFood")
                return;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("流程编号：" + notification.TaskId);
            Console.ResetColor();
            var args = notification.TaskPayload!;
            var foodName = args.GetValue<string>("Food");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("开始准备 {0}...", foodName);
            Console.ResetColor();
            await Task.Delay(1000, cancellationToken);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("已经做好!");
            Console.ResetColor();
            //await _taskReporter.ReportCompletionAsync(notification.TaskId, foodName, cancellationToken);
        }
    }
    public class FoodDeliveryTaskHandler : INotificationHandler<RunTaskRequest>
    {
        private readonly ITaskReporter _taskReporter;

        public FoodDeliveryTaskHandler(ITaskReporter taskReporter)
        {
            _taskReporter = taskReporter;
        }

        public async Task HandleAsync(RunTaskRequest notification, CancellationToken cancellationToken)
        {
            if (notification.TaskName != "FoodDelivery")
                return;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("流程编号：" + notification.TaskId+"，开始送货");
            Console.ResetColor();
            var args = notification.TaskPayload!;
            var address = args.GetValue<string>("address");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("正在送货到{0}...", address);
            Console.ResetColor();
            await Task.Delay(1000, cancellationToken);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("已送达!");
            Console.ResetColor();
            //await _taskReporter.ReportCompletionAsync(notification.TaskId, foodName, cancellationToken);
        }
    }
}
