using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;

namespace MyWorkFlow
{
    [Activity("MyWorkFlow", Description="通用文件系统",DisplayName ="文件",Category ="四哟")]
    public class CommonFileActivity : Activity
    { 
        protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
        {
            context.SetProperty<string>("name", "六点半");
            await base.ExecuteAsync(context);
            await context.CompleteActivityAsync();
        }
    }
}
