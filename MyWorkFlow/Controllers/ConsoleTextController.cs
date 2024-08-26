
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Contracts;
using Elsa.Workflows.Management.Services;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime.Activities;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Threading;
namespace MyWorkFlow.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConsoleTextController : ControllerBase
    {
        private readonly IWorkflowRunner _workflowRunner;
        private readonly IWorkflowRuntime _workflowRuntime;
        private readonly ITaskReporter _taskReporter;
        private readonly IActivityInvoker _activityInvoker;
        public ConsoleTextController(IWorkflowRunner workflowRunner, IWorkflowRuntime workflowRuntime, ITaskReporter taskReporter)
        {
            _workflowRunner = workflowRunner;
            _workflowRuntime = workflowRuntime;
            _taskReporter = taskReporter;
        }
        [HttpGet]
        public async Task WriteLine(string text)
        {

            var workflow = new Workflow
            {
                Root = new Sequence
                {
                    Activities =
                        {
                            new WriteLine($"Hello {text}!"),
                            new WriteLine("Goodbye cruel world!")
                        }
                }
            };
            var definitionId = workflow.Id;
            //var result = await _workflowRuntime.StartWorkflowAsync(definitionId);
            await _workflowRunner.RunAsync(workflow);
        }
        private static RunWorkflowResult result = null;
        [HttpGet("customer")]
        public async Task<Workflow> GenWorkFlow()
        {
            var workflow = new Workflow
            {
                Root = new Sequence
                {
                    Activities =
                        {
                            new WriteLine("Starting workflow..."),
                            new MyEvent(), // This will block further execution until the MyEvent's bookmark is resumed. 
                            new WriteLine("Event occurred!")
                        }
                }
            };
            result = await _workflowRunner.RunAsync(workflow);

            return workflow;
        }
        [HttpGet("trigger")]
        public async Task<Workflow> TriggerWorkflow()
        {
            var workflowState = result.WorkflowState;
            var bookmark = workflowState.Bookmarks.Single(); // Get the bookmark that was created by the MyEvent activity.
            var options = new RunWorkflowOptions(); options.BookmarkId = bookmark.Id;

            // Resume the workflow.
            await _workflowRunner.RunAsync(result.Workflow, workflowState, options);
            return null;
        }

        [HttpGet("finish")]
        public async Task FinishWorkflow(string taskId)
        {
            await _taskReporter.ReportCompletionAsync(taskId, "汉堡");
        }
    }
    public class MyEvent : Activity
    {
        protected override void Execute(ActivityExecutionContext context)
        {
            // Create a bookmark. The created bookmark will be stored in the workflow state.
            context.CreateBookmark();

            // This activity does not complete until the event occurs.
        }
    }
    public class Greeter : WorkflowBase
    {
        public void Build(IWorkflowBuilder builder)
        {
            // Initialize a variable named "Message" with a default value of "Hello World!".
            var message = builder.WithVariable<string>("Hello World!").WithMemoryStorage();

            // Output the message to the console.
            builder.Root = new WriteLine(context => message.Get(context));

        }
    }
}
