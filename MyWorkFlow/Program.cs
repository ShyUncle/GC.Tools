using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.Extensions;
using Elsa.Studio.Core.BlazorServer.Extensions;
using Elsa.Studio.Dashboard.Extensions;
using Elsa.Studio.Extensions;
using Elsa.Studio.Login.BlazorServer.Extensions;
using Elsa.Studio.Login.HttpMessageHandlers;
using Elsa.Studio.Shell.Extensions;
using Elsa.Studio.Workflows.Designer.Extensions;
using Elsa.Studio.Workflows.Extensions;
using Microsoft.EntityFrameworkCore;
namespace MyWorkFlow
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            // Register Razor services.
            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor(options =>
            {
                // Register the root components.
                options.RootComponents.RegisterCustomElsaStudioElements();
            });
            // Register shell services and modules.
            builder.Services.AddCore();
            builder.Services.AddShell(options => builder.Configuration.GetSection("Shell").Bind(options));
            builder.Services.AddRemoteBackend(
                elsaClient => elsaClient.AuthenticationHandler = typeof(AuthenticatingApiHttpMessageHandler),
                options => builder.Configuration.GetSection("Backend").Bind(options));
            builder.Services.AddLoginModule();
            builder.Services.AddDashboardModule();
            builder.Services.AddWorkflowsModule();
            // Configure SignalR.
            builder.Services.AddSignalR(options =>
            {
                // Set MaximumReceiveMessageSize to handle large workflows.
                options.MaximumReceiveMessageSize = 5 * 1024 * 1000; // 5MB
            });
            builder.Services.AddDbContext<SchoolContext>(options =>
            {
                options.UseElsaSqlServer(typeof(Program).Assembly, builder.Configuration.GetConnectionString("SchoolContext")!);
                options.UseSqlServer(builder.Configuration.GetConnectionString("SchoolContext"));
            });
            builder.Services.AddHandlersFrom<Program>();
            builder.Services.AddElsa(elsa =>
            {
                // Configure Management layer to use EF Core.
                elsa.UseWorkflowManagement(management => management.UseEntityFrameworkCore());

                // Configure Runtime layer to use EF Core.
                elsa.UseWorkflowRuntime(runtime => runtime.UseEntityFrameworkCore());

                // Default Identity features for authentication/authorization.
                elsa.UseIdentity(identity =>
                {
                    identity.TokenOptions = options => options.SigningKey = "sufficiently-large-secret-signing-key"; // This key needs to be at least 256 bits long.
                    identity.UseAdminUserProvider();
                });

                // Configure ASP.NET authentication/authorization.
                elsa.UseDefaultAuthentication(auth => auth.UseAdminApiKey());

                // Expose Elsa API endpoints.
                elsa.UseWorkflowsApi();

                // Setup a SignalR hub for real-time updates from the server.
                elsa.UseRealTimeWorkflows();

                // Enable JavaScript workflow expressions
                elsa.UseJavaScript(options => options.AllowClrAccess = true);
               
                elsa.AddWorkflowsFrom<Program>(); 
                elsa.UseHttp();
                // Register custom webhook definitions from the application, if any.
                elsa.UseWebhooks(webhooks => webhooks.WebhookOptions = options => builder.Configuration.GetSection("Webhooks").Bind(options));

            });
            // Configure CORS to allow designer app hosted on a different origin to invoke the APIs.
            builder.Services.AddCors(cors => cors
                .AddDefaultPolicy(policy => policy
                    .AllowAnyOrigin() // For demo purposes only. Use a specific origin instead.
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders("x-elsa-workflow-instance-id"))); // Required for Elsa Studio in order to support running workflows from the designer. Alternatively, you can use the `*` wildcard to expose all headers.
                                                                          // Add Health Checks.
            builder.Services.AddHealthChecks();
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            } 
            // Configure web application's middleware pipeline.
            app.UseCors();
            app.UseRouting(); // Required for SignalR.
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.MapBlazorHub();
            app.MapFallbackToPage("/_Host");

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseWorkflowsApi(); // Use Elsa API endpoints.
            app.UseWorkflows(); // Use Elsa middleware to handle HTTP requests mapped to HTTP Endpoint activities.
            app.UseWorkflowsSignalRHubs(); // Optional SignalR integration. Elsa Studio uses SignalR to receive real-time updates from the server. 

            app.MapControllers();

            app.Run();

        }
    }
}
