using Microsoft.AspNetCore.Mvc;

namespace MMAService.Controllers
{

    public class AdminRequest
    {
        public string User { get; set; }
        public string Twofactor { get; set; }
        public int Expire { get; set; }
    }

    public class AdminReply
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    /*
     * Use this controller when no controller is specified in the address
     */
    [Route("/")]
    [ApiController]
    // ControllerBase instead of Controller because minimal framework
    public class AdminController : ControllerBase
    {

        /*
         * user is filled by MVC when a request is incoming
         * /check-admin?user=Data
         * Verify against processlist to see if requesting user has MMA-win open?
         * Better yet, take the username and domain from the user running the MMA-win process /Robert
         */
        [HttpGet("check-admin")]
        public ActionResult<AdminReply> CheckAdmin([FromQuery(Name = "user")] string user)
        {
            var (success, message) = Program.CheckCanBecomeAdmin(user);
            return new AdminReply() { Success = success, Message = message };
        }

        /*
         * AdminRequest is filled by MVC when a request is incoming
         * all fields are input from the webrequest
         */
        [HttpPost("add-admin")]
        public ActionResult<AdminReply> AddAdmin(AdminRequest req)
        {
            if (req.Expire >= 1 && req.Expire <= 60)
            {
                var (success, message) = Program.AddAdmin(req.User, req.Twofactor, req.Expire);
                return new AdminReply() { Success = success, Message = message };
            }
            else
            {
                return new AdminReply() { Success = false, Message = "Length of session is not within range." };
            }
        }
    }
}
