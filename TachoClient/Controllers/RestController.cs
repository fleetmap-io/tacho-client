using Microsoft.AspNetCore.Mvc;

namespace TachoClient.Controllers
{
    [Route("")]
    [ApiController]
    public class RestController : Controller
    {

        [HttpGet]
        public IActionResult Get()
        {
            return Ok("ok");
        }

        [HttpPost]
        public IActionResult Send(Device device, string apdu)
        {
            //var companyId = device.attributes.clientId;
            return Ok("9000");
        }

        public class Device
        {
            public DeviceAttributes attributes { get; set; }
        }

        public class DeviceAttributes
        {
            public int clientId { get; set; }
        }
    }
}
