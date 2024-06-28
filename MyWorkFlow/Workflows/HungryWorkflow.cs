
using Elsa.Expressions.Models;
using Elsa.Http;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Contracts;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime.Activities;
using Polly;
namespace MyWorkFlow.Workflows
{

    public class HungryWorkflow : WorkflowBase
    {
        protected override void Build(IWorkflowBuilder builder)
        {
            var deliveredFood = builder.WithVariable<string>();
            var step1 = new HttpEndpoint
            {
                Path = new("/hungry"),
                SupportedMethods = new(new[] { HttpMethods.Post }),
                ParsedContent = new(deliveredFood),
                CanStartWorkflow = true
            };
            var step2 = new WriteLine(context => "库存!" + deliveredFood.Get(context));

            var step3 = new RunTask("OrderFood")
            {
                Payload = new(new Dictionary<string, object> { ["Food"] = "Pizza" }),
                Result = new Output<object>(deliveredFood)
            };
            var step4 = new RunTask("FoodDelivery")
            {
                Payload = new(new Dictionary<string, object> { ["address"] = "科技园" }),
                Result = new Output<object>(deliveredFood)
            };
            var step5 = new WriteLine(context => $"开吃 the {deliveredFood.Get(context)}");
            var step6 = new WriteLine("好吃!");
            var jumpStep =
                new If(context => deliveredFood.Get(context) == "0")
                {
                    Then = new Sequence
                    {
                        Activities =
                        {
                            step3,
                            step4,
                        }
                    },
                    Else = step4
                }
            ;

            var join = new FlowJoin() { Name = "join" };
            string GetRupees(ExpressionExecutionContext context) => deliveredFood.Get(context)!;
            builder.Root = new Flowchart
            {
                Start = step1,
                Activities =
                {
                    step1,
                    step2,
                    jumpStep,
                   // join,
                    step5,
                    step6
                },
                Connections =
                {
                    new Connection(step1,step2),
                    new Connection(step2,jumpStep),
                    new Connection(jumpStep,step5),
                   // new Connection(join,step5),
                    new Connection(step5,step6)
                }
            };
        }
    }
}
