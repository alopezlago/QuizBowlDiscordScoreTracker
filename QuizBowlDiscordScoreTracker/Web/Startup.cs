using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QuizBowlDiscordScoreTracker.Web
{
    public class Startup
    {
        private const string UrlSetting = "webBaseUrl";

        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        [SuppressMessage(
            "Performance",
            "CA1822:Mark members as static",
            Justification = "Used by ASP.Net Core, and must be an instance method")]
        public void ConfigureServices(IServiceCollection services)
        {
            // TODO: Initialize the BotConfigurationContext here with AddDbContext. You need a service scope to use it,
            // and we'd need to move away from using Program.cs and making this a proper IHostedService. See
            // https://stackoverflow.com/questions/51618406/cannot-consume-scoped-service-mydbcontext-from-singleton-microsoft-aspnetcore
            // TODO: See if there's a way we can avoid initializing SignalR if the url setting isn't set. I suspect
            // it's not possible, since we pass in the IHubContext to the Bot constructor, and it probably needs a
            // registered type for the interface
            services.AddSignalR();

            string url = this.GetUrl();
            if (!IsUrlValid(url, out Uri uri))
            {
                // Nothing to configure, since we're not setting up the site
                return;
            }

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                services.AddHsts(options =>
                {
                    options.IncludeSubDomains = true;
                    options.MaxAge = TimeSpan.FromDays(60);
                });
            }
        }

        [SuppressMessage(
            "Performance",
            "CA1822:Mark members as static",
            Justification = "Used by ASP.Net Core, and must be an instance method")]
        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            // TODO: See if there's a way we can avoid initializing SignalR if the url setting isn't set
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseFileServer();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<MonitorHub>("/hub");
            });
        }

        private string GetUrl()
        {
            return this.Configuration.GetValue(UrlSetting, string.Empty);
        }

        private static bool IsUrlValid(string url, out Uri uri)
        {
            uri = null;
            return string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out uri);
        }
    }
}
