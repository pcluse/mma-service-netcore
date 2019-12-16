using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.WindowsServices;
using System.ServiceProcess;

namespace MMAService {

    internal class MMAWebHostService : WebHostService
    {

        public MMAWebHostService(IWebHost host) : base(host)
        {
            CanPauseAndContinue = false;
            CanShutdown = false;
            CanStop = false;
            CanHandleSessionChangeEvent = true;
        }

        protected override void OnStarting(string[] args)
        {
            base.OnStarting(args);
        }

        protected override void OnStarted()
        {
            base.OnStarted();
            Program.OnStart();
        }

        protected override void OnStopping()
        {  
            Program.CleanupAdminGroup();
            base.OnStopping();
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            if (changeDescription.Reason == SessionChangeReason.SessionLogoff )
            {
                Program.CleanupAdminGroup();
            }
            base.OnSessionChange(changeDescription);
        }

        protected override void OnShutdown()
        {
            base.OnShutdown();
        }
    }

    // https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods
    // Magic to add RunAsCustomService as a method to IWebHost
    public static class MMAWebHostServiceExtensions
    {
        
        public static void RunAsCustomService(this IWebHost host)
        {
            var webHostService = new MMAWebHostService(host);
            ServiceBase.Run(webHostService);
        }
    }
}
