using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Timers;

namespace MMAService
{
    public class Program
    {
        private static Backend backend;
        internal static ILogger logger;
        internal static CCMCollectionVariables MMAVars;
        private static bool SharedPC = false;
        private static bool isTest = false;
        private static Timer timer = new Timer();
        private static Timer netTimer = new Timer();
        private static string TemporaryAdmin = "";
        private static DateTime DontCleanUpBefore;
        private static bool netOK = false;
        private static int expire = 15; // Admin rights will expire
        private static Dictionary<string, PrerequisitesReply> userToPrerequisites = new Dictionary<string, PrerequisitesReply>();


        public static void Main(string[] args)
        {   
            // https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-2.1
            var isService = !(Debugger.IsAttached || args.Contains("--console"));
            isTest = args.Contains("--test");
            // isTest = true;
            // What should you be able to give as argument to this function? I would prefer not to let the end-user give it raw input. /Robert
            var builder = CreateWebHostBuilder(args.Where(arg => arg != "--console" && arg != "--test").ToArray());
            var host = builder.Build();

            logger = host.Services
               .GetRequiredService<ILogger<MMAWebHostService>>();

            logger.LogInformation("Make Me Admin service starting...");

            if (isTest)
            {
                logger.LogWarning("Running as test");
            }

            if (isService)
            {
                try
                {
                    host.RunAsCustomService();
                }
                catch(Exception e)
                {
                    logger.LogWarning("Exception: {0}", e.ToString());
                }
                
            }
            else
            {
                logger.LogInformation("Not running as a service");
                /*
                 * This is used when not running as a service.
                 * When running as a service OnStart is called from MMAWebHostService.OnStarted
                 * When running as a service CleanupAdminGroup is called from MMAWebHostService.OnStopping
                 * After OnStart we run the WebHost and when it has stopped we clean the admin group.
                 */
                try
                {
                    OnStart();
                    host.Run();
                } catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine("Press enter to continue...");
                    Console.Read();
                }
            }
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                // Set content root to install directory. Must be set and can't be null
                // Default value is build directory (JD)
                .UseContentRoot(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location))
                .UseKestrel(options =>
                {
                    // We will only listen to localhost
                    options.ListenLocalhost(16666);
                    // There is not much use to have alot of connections
                    // options.Limits.MaxConcurrentConnections = 10;
                    // Set the maximum size of the request body to a low value
                    options.Limits.MaxRequestBodySize = 200000;
                    options.AddServerHeader = true; //HTTP port
                })
                //.UseUrls("http://localhost:6666")
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.AddEventSourceLogger();
                })
                .UseStartup<Startup>();


        internal static void OnStart()
        {
            try {
                SharedPC = Computer.IsSharedPC();
            }
            catch (System.Management.ManagementException e)
            {
                logger.LogError("Checking SharedPCMode failed :{0}", e.Message);
            }

            netTimer.Elapsed += new ElapsedEventHandler(CheckNetwork);
            netTimer.Interval = 5 * 1000; //number in milliseconds (every 5 seconds) 
            netTimer.Enabled = true;

            timer.Elapsed += new ElapsedEventHandler(OnElapsedTime);
            timer.Interval = 60 * 5 * 1000; //number in milliseconds (every 5 minutes) 
            timer.Enabled = true;

            // TODO - Add a function named PrecheckService which makes sure everything is available at runtime
            MMAVars = new CCMCollectionVariables(isTest);
            CleanupAdminGroup();
        }
        internal static void CheckNetwork(object source, ElapsedEventArgs e) {
            // Make sure we have netowrk before operation starts
            netOK = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            if (netOK) {
                netTimer.Interval = 60 * 5 * 1000; //number in milliseconds (every 5 minutes)
            }
            else {
                netTimer.Interval = 5 * 1000; //number in milliseconds (every 5 seconds)
            }
            if (isTest) {
                logger.LogInformation(string.Format("Network is {0}, wait {1} milliseconds...",netOK,netTimer.Interval));
            }
        }
        internal static bool IsMMAPossible() {
            try {
                var MMAClientEnabled = MMAVars.Get("MMAClientEnabled");
                switch (MMAClientEnabled)
                {
                    case null:
                        logger.LogWarning("MMAClientEnabled is not set");
                        return false;
                    case "0":
                        logger.LogWarning("Make me admin client has not been enabled for this computer");
                        return false;
                    case "1":
                        logger.LogInformation("Make me admin client is enabled for this computer");
                        break;
                    default:
                        logger.LogError("MMAClientEnabled = '{0}' is not a valid value", MMAClientEnabled);
                        return false;
                }

                var MMBackendUrl = MMAVars.Get("MMABackendUrl");
                if (String.IsNullOrEmpty(MMBackendUrl))
                {
                    logger.LogWarning("MMABackendUrl not set");
                    return false;
                }
                
                var MMAApiKey = MMAVars.Get("MMAApiKey2");
                if (String.IsNullOrEmpty(MMAApiKey))
                {
                    logger.LogWarning("MMAApiKey2 not set");
                    return false;
                }

                backend = new Backend(MMAVars.Get("MMABackendUrl"), MMAVars.Get("MMAApiKey2"));
            }
            catch (System.Management.ManagementException me) {
                logger.LogError(me.Message);
                return false;
            }
            return true;
        }

        private static void OnElapsedTime(object source, ElapsedEventArgs e)
        {
            // Check if temporary admin has timed out
            if (DateTime.Now > DontCleanUpBefore) {
                CleanupAdminGroup();
                TemporaryAdmin = "";
            }
        }

        internal static void CleanupAdminGroup()
        {
            try {

                var group = MMAVars.Get("MMALocalAdminGroup");
                
                if (String.IsNullOrEmpty(group))
                {
                    // Empty should generate information
                    // Variable missing should generate error
                    logger.LogError("Something is wrong with MMALocalAdminGroup, either it is not present or it is empty.");
                    return;
                }

                // If TemporaryAdmin is set, delete it first
                int ret;
                if (TemporaryAdmin != "")
                {
                    ret = LocalGroup.DeleteMember(LocalGroup.AdminGroupName, TemporaryAdmin);
                    
                    if (ret == 0)
                    {
                        logger.LogInformation("Deleted {0} from {1}", TemporaryAdmin, LocalGroup.AdminGroupName);
                    }
                    else
                    {
                        logger.LogError("Error #{0} deleting {1} from {2}", ret, TemporaryAdmin, LocalGroup.AdminGroupName);
                    }
                    TemporaryAdmin = "";
                }

                // Delete the others
                bool groupIsMember = false;
                var members = LocalGroup.GetMembers(LocalGroup.AdminGroupName);
                foreach (var member in members)
                {
                    if (member == LocalGroup.AdminUserName)
                    {
                        // Do nothing
                    }
                    else if (member == group)
                    {
                        groupIsMember = true;
                    }
                    else
                    {
                        ret = LocalGroup.DeleteMember(LocalGroup.AdminGroupName, member);
                        if (ret == 0)
                        {
                            logger.LogInformation("Deleted {0} from {1}", member, LocalGroup.AdminGroupName);
                        }
                        else
                        {
                            logger.LogError("Error #{0} deleting {1} from {2}", ret, member, LocalGroup.AdminGroupName);
                        }                       
                    }
                }

                if (!groupIsMember)
                {
                    // Make sure that the centrally controlled group is a member of Administrators
                    // Try to add it and if successfull report it
                    ret = LocalGroup.AddMember(LocalGroup.AdminGroupName, group);
                    if (ret == 0)
                    {
                        logger.LogInformation("Added {0} to {1}.", group, LocalGroup.AdminGroupName);
                    }
                    else if (ret == 1378)
                    {
                        logger.LogInformation("Group {0} is already a member of {1}.", group, LocalGroup.AdminGroupName);
                    }
                    else
                    {
                        logger.LogError("Failed to add {0} to {1} with errorcode {2}.", group, LocalGroup.AdminGroupName, ret);
                    }
                }
            } catch (System.Management.ManagementException me) {
                logger.LogError(me.Message);
                return;
            } catch (System.Exception e) {
                logger.LogError(e.Message);
                return;
            }
        }

        /*
         * Checks against expire is done in AdminController since thats the entrypoint for the data
         */
        public static bool ExpireFromAdminGroup(string user, int expire)
        {
            if (TemporaryAdmin != "" && TemporaryAdmin != user)
            {
                LocalGroup.DeleteMember(LocalGroup.AdminGroupName, TemporaryAdmin);
                TemporaryAdmin = "";
                logger.LogInformation("Deleted {0} from {1} because {2} became admin", TemporaryAdmin, LocalGroup.AdminGroupName, user);
            }

            if (user == TemporaryAdmin)
            {
                // Set a new timeout
                DontCleanUpBefore = DateTime.Now.AddMinutes(expire);
                logger.LogInformation("New timeout for user {0} for {1} minutes", TemporaryAdmin, expire);
                return true;
            }

            var ret = LocalGroup.AddToAdminGroup(user);
            if (ret != 0 && ret != 1378 /* Already member */)
            {
                logger.LogWarning("User {0} couldn't be added to admin group ({1})", user, ret);
                return false;
            }

            TemporaryAdmin = user;
            DontCleanUpBefore = DateTime.Now.AddMinutes(expire);

            logger.LogInformation("User {0} was added to the local administrators group for {1} minutes", user, expire);
            return true;
        }

        private static (bool, string) PrecheckUserRequest(string user)
        {
            // Does this go nested groups? Nope!
            if (LocalGroup.IsAdmin(user))
            {
                // Need better handling of messages as this is not an error.
                // If we return it as true (not an error) we can not distinguish it from other success messages.
                //return (true, "You are already admin");
                return (false, "error.already_admin");
            }

            if (isTest)
            {
                return (true, "");
            }
            List<string> owners;
            try
            {
                owners = Computer.GetPrimaryUsers();
            }
            catch (System.Management.ManagementException e)
            {
                logger.LogError("Missing SCCM namespace.{0}", e.Message);
                return (false, "error.generic_error");
            }
            string UpperUsername = user.ToUpper();
            if (owners.Count() > 0 && !owners.Contains(UpperUsername))
            {
                // First one in list should be owner, maybe change GetPrimaryUsers to GetOwner?
                logger.LogWarning("User {0} is not owner, owner is {1}", user, owners[0]);
                return (false, "error.not_owner");
            }
            if (!netOK) {
                return (false, "error.network_down");
            }
            return (true, "");
        }

        internal static (bool, string) CheckPrerequisites()
        {
            var loggedOnUsers = Computer.GetLoggedInUsers();
            if (loggedOnUsers.Count != 1)
            {
                // Should this be banned if a computer is used by multiple users?
                return (false, "error.multiple_logged_in");
            }
            var user = loggedOnUsers[0];
            var (success, message) = PrecheckUserRequest(user);
            if (! success)
            {
                logger.LogWarning("Check admin for {0}: {1}", user, message);
                return (false, message);
            }

            var prerequisitesTask = backend.GetPrerequisites(user);

            try
        {
                var prerequisites = prerequisitesTask.GetAwaiter().GetResult();
                if (userToPrerequisites.ContainsKey(user)) {
                    userToPrerequisites[user] = prerequisites;
                } else
                {
                    userToPrerequisites.Add(user, prerequisites);
                }
                
                if (!prerequisites.PLSLucatLocalAdministrator)
                {
                    logger.LogWarning("User {0} must apply for admin permission in lucat", user);
                    return (false, "error.not_applied_for_admin_permission_pls");
                }
                
                if (prerequisites.preferredService == null)
                {
                    logger.LogWarning("User {0} has neither strong password or Freja eID activated", user);
                    return (false, "error.no_service_configured");
                }
                logger.LogInformation("CheckLucatAdmin: true, preferredService: {0}", prerequisites.preferredService);
                return (true, prerequisites.preferredService);
                
            }
            catch (HttpRequestException e)
            {
                logger.LogWarning("Error for {0}: {1}", user, e.ToString());
                return (false, "error.service_down");
            }
        }

        
        /*
         * This function verifies the twofactor authentication code and adds the user to the group if it was successful
         */
        internal static (bool, bool, string) ValidateTotp(string twofactor)
        {
            var user = Computer.GetLoggedInUsers()[0];
            var prerequisites = userToPrerequisites[user];
            if (prerequisites == null)
            {
                logger.LogWarning("User {0} prerequisites not found", user);
                return (false, false, "error.prerequisites");
            }
            if (!prerequisites.PLSLucatLocalAdministrator)
            {
                return (false, false, "error.not_applied_for_admin_permission_pls");
            }

            try
            {
                var task = backend.ValidateTotp(user, twofactor);
                
                var validated = task.GetAwaiter().GetResult();
                if (!validated)
                {
                    logger.LogWarning("{0}: Could not verify totp code", user);
                    return (true, false, "");
                }
            }
            catch (Newtonsoft.Json.JsonReaderException  e)
            {
                logger.LogWarning("{0}: Could not parse response, error {1}", user, e.ToString());
                return (false, false, "error.generic_error");
            }
            /* catch ( e)
            {
                logger.LogWarning("{0}: Could not verify the code, error {1}", user, e.ToString());
                return (false, "Could not verify the code. Are you connected to internet?");
            } */
            var wasAdded = ExpireFromAdminGroup(user, expire);
            if (!wasAdded)
            {
                return (false, false, "error.valid_code_but_not_made_admin");
            }
            return (true, true, "");
        }

        internal static (bool, bool, string) ValidateFreja()
        {
            var user = Computer.GetLoggedInUsers()[0];
            var prerequisites = userToPrerequisites[user];
            if (prerequisites == null)
            {
                logger.LogWarning("User {0} prerequisites not found", user);
                return (false, false, "error.prerequisites");
            }
            if (!prerequisites.PLSLucatLocalAdministrator)
            {
                return (false, false, "error.not_applied_for_admin_permission_pls");
            }

            try
            {
                var task = backend.ValidateFreja(user);

                var validated = task.GetAwaiter().GetResult();
                if (!validated)
                {
                    logger.LogWarning("{0}: Could not validate freja", user);
                    return (true, false, "");
                }
            }
            catch (Newtonsoft.Json.JsonReaderException e)
            {
                logger.LogWarning("{0}: Could not parse response, error {1}", user, e.ToString());
                return (false, false, "error.generic_error");
            }
            /* catch ( e)
            {
                logger.LogWarning("{0}: Could not verify the code, error {1}", user, e.ToString());
                return (false, "Could not verify the code. Are you connected to internet?");
            } */
            var wasAdded = ExpireFromAdminGroup(user, expire);
            if (!wasAdded)
            {
                return (false, false, "error.validated_but_not_made_admin");
            }
            return (true, true, "");
        }

        internal static (bool, bool, string) ValidateTechnician(string technicianUid)
        {
            var user = Computer.GetLoggedInUsers()[0];
            try
            {
                var task = backend.ValidateFreja(technicianUid);

                var validated = task.GetAwaiter().GetResult();
                if (!validated)
                {
                    logger.LogWarning("{0}: Could not validate freja", user);
                    return (true, false, "");
                }
            }
            catch (Newtonsoft.Json.JsonReaderException e)
            {
                logger.LogWarning("{0}: Could not parse response, error {1}", user, e.ToString());
                return (false, false, "error.generic_error");
            }
            /* catch ( e)
            {
                logger.LogWarning("{0}: Could not verify the code, error {1}", user, e.ToString());
                return (false, "Could not verify the code. Are you connected to internet?");
            } */
            var wasAdded = ExpireFromAdminGroup(user, expire);
            if (!wasAdded)
            {
                return (false, false, "error.validated_but_not_made_admin");
            }
            return (true, true, "");
        }
    }
}
