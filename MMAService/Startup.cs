using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;

namespace MMAService
{
    public class Startup
    {
        readonly string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            //services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            // https://codingblast.com/using-web-api-asp-net-core-without-mvc-specific-stuff/
            // Minimal webserver without alot of stuff (JD)
            var mvcCoreBuilder = services.AddMvcCore();

            mvcCoreBuilder
                .AddFormatterMappings()
                .AddNewtonsoftJson()
                .AddCors();

            services.AddLogging(builder => builder
                .AddConsole()
                .AddEventLog(new EventLogSettings
                {
                    SourceName = "MMA"
                })
                .AddDebug());
            services.AddCors(options =>
            {
                options.AddPolicy(name: MyAllowSpecificOrigins,
                                  builder =>
                                  {
                                      builder.WithOrigins("https://staging.lth.lu.se",
                                                          "https://mma.lu.se");
                                  });
            });

            services.AddControllers(options => options.EnableEndpointRouting = false);
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            // Doesn't use https in application
            //app.UseHttpsRedirection();

            app.UseCors(MyAllowSpecificOrigins);
            app.UseMvc();
        }
    }
}
