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
using System.Net;
using System.Net.Sockets;

namespace MMAService
{
    public class Program
    {
        private static RestClient client;
        internal static ILogger logger;
        internal static CCMCollectionVariables MMAVars;
        private static bool SharedPC = Computer.IsSharedPC();
        private static bool isTest = false;
        private static Timer timer = new Timer();
        private static Timer netTimer = new Timer();
        private static string TemporaryAdmin = "";
        private static DateTime DontCleanUpBefore;
        private static bool netOK = false;


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
                    options.ListenLocalhost(6666);
                    // There is not much use to have alot of connections
                    options.Limits.MaxConcurrentConnections = 1;
                    // Set the maximum size of the request body to a low value
                    // the maximum size we want to accept is the sizeof AdminController.AdminRequest ((string,domain\username)username, (string, max 6 characters)TwoFactor and (int)expire)
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
            logger.LogInformation(string.Format("Network is {0}, wait {1} milliseconds...",netOK,netTimer.Interval));
        }
        internal static bool IsAdminPossible() {

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
                    logger.LogWarning("Make me admin client is enabled for this computer");
                    break;
                default:
                    logger.LogError("MMAClientEnabled = '{0}' is not a valid value", MMAClientEnabled);
                    break;
            }

            var MMAServer = MMAVars.Get("MMAServer");
            if (String.IsNullOrEmpty(MMAServer))
            {
                logger.LogWarning("MMAServer not set");
                return false;
            }
            
            var MMAApiKey = MMAVars.Get("MMAApiKey");
            if (String.IsNullOrEmpty(MMAApiKey))
            {
                logger.LogWarning("MMAApiKey not set");
                return false;
            }
            
            var MMAServerThumbprint = MMAVars.Get("MMAServerThumbprint");
            if (String.IsNullOrEmpty(MMAServerThumbprint))
            {
                logger.LogWarning("MMAServerThumbprint not set");
                return false;
            }

            client = new RestClient("https://" + MMAServer, MMAServerThumbprint, MMAApiKey);
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
            if (! IsAdminPossible())
            {
                return (false, "Make me admin is not allowed on this computer");
            }
            // Does this go nested groups? Nope!
            if (LocalGroup.IsAdmin(user))
            {
                // Need better handling of messages as this is not an error.
                // If we return it as true (not an error) we can not distinguish it from other success messages.
                //return (true, "You are already admin");
                return (false, "Your account is already a member of the administrators group on this computer.");
            }

            var loggedOnUsers = Computer.GetLoggedInUsers();
            if (loggedOnUsers.Count != 1)
            {
                // Should this be banned if a computer is used by multiple users?
                return (false, "Multiple users are logged on to this computer.\nElevation is not allowed when other users are logged on.");
            }
            if (!loggedOnUsers.Contains(user))
            {
                // This should only happen if someone is trying to use this service without the MMA-client
                return (false, "The specified user is not logged into this computer.");
            }
            if (isTest)
            {
                return (true, "");
            }
            List<string> owners = null;
            try
            {
                owners = Computer.GetPrimaryUsers();
            }
            catch (System.Management.ManagementException e)
            {
                logger.LogError("Missing SCCM namespace.{0}", e.Message);
                return (false, "Unable to determine the owner of this computer.");
            }
            string UpperUsername = user.ToUpper();
            if (owners.Count() > 0 && !owners.Contains(UpperUsername))
            {
                // First one in list should be owner, maybe change GetPrimaryUsers to GetOwner?
                logger.LogWarning("User {0} tried to become admin but was denied because the user is not the owner of this computer.", user);
                return (false, String.Format("Only the user set as owner can become admin of this computer.\nCurrent owner is {0}.", owners[0]));
            }
            if (!netOK) {
                return (false, "Network is not available right now.\nTry again when network connectivity has been restored.");
            }
            return (true, "");
        }

        internal static (bool, string) CheckCanBecomeAdmin(string user)
        {
            var (success, message) = PrecheckUserRequest(user);
            if (! success)
            {
                logger.LogWarning("Check admin for {0}: {1}", user, message);
                return (false, message);
            }
            var computerName = Environment.MachineName;

            var taskCheckLucatAdmin = client.CheckLucatAdmin(user);
            var taskCheckTFA = client.CheckTFA(user);

            try
            {
                var checkLucatAdmin = taskCheckLucatAdmin.GetAwaiter().GetResult();
                if (!checkLucatAdmin)
                {
                    logger.LogWarning("User {0} was not allowed to become admin as the right has not been granted.", user);
                    return (false, "You are not allowed to become admin! Did you request the right it in lucat?");
                    // We need to handle this! API returns false if user is not 2FA activated.
                    // Demo mode below
                    //return (true, "");
                }
                
                var checkTFA = taskCheckTFA.GetAwaiter().GetResult();
                if (!checkTFA)
                {
                    return (false, "User has not activated twofactor authentication");
                }
                logger.LogInformation("CheckLucatAdmin: true, CheckTFA: true");
                return (true, "");
                
            }
            catch (HttpRequestException e)
            {
                logger.LogWarning("Error for {0}: {1}", user, e.ToString());
                return (false, "Are you connected to internet?");
            }
        }

        public static string[] GetLocalIPAddresses() {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            string[] result = new string[host.AddressList.Length];
            int i = 0;
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    result[i] = ip.ToString();
                }
                i++;
            }
            return result;
            //throw new Exception("No network adapters with an IPv4 address in the system!");
        }
        /*
         * This function verifies the twofactor authentication code and adds the user to the group if it was successful
         */
        internal static (bool, string) AddAdmin(string user, string twofactor, int expire)
        {
            var (success, message) = PrecheckUserRequest(user);
            if (!success)
            {
                logger.LogWarning("Add admin for {0}: {1}", user, message);
                return (false, message);
            }

            //var computerName = Environment.MachineName;

            var task = client.Verify(user, twofactor);
            try
            {
                var verified = task.GetAwaiter().GetResult();
                if (!verified)
                {
                    logger.LogWarning("{0}: Could not verify your code", user);
                    return (false, "Could not verify the code, please try again.");
                }
            }
            catch (Newtonsoft.Json.JsonReaderException  e)
            {
                logger.LogWarning("{0}: Could not parse response, error {1}", user, e.ToString());
                return (false, "Could not verify the code. Service not available?");
            }
            /* catch ( e)
            {
                logger.LogWarning("{0}: Could not verify the code, error {1}", user, e.ToString());
                return (false, "Could not verify the code. Are you connected to internet?");
            } */
            var wasAdded = ExpireFromAdminGroup(user, expire);
            if (!wasAdded)
            {
                return (false, "Couldn't make you an admin");
            }
            return (true, "");
        }
    }
}
