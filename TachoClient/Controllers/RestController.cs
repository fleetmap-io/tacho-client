using Microsoft.AspNetCore.Mvc;
using PCSC;

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
        public IActionResult SendApdu(SendApduRequest req)
        {
            try
            {
                var deviceId = req.device.id;
                var companyId = req.device.attributes.clientId;
                var icc = IccHelper.GetIcc(companyId);
                if (!IccHelper.LockIcc(icc, deviceId))
                {
                    return Conflict($"Cannot lock card with icc:{icc} for deviceId:{deviceId}");
                }
                var readerName = IccHelper.GetReaderName(icc);

                using (var reader = Program.context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    var apduBytes = Enumerable.Range(0, req.apdu.Length / 2).Select(x => Convert.ToByte(req.apdu.Substring(x * 2, 2), 16)).ToArray();
                    var apduResponseBytes = Program.SendApduToSmartCard(reader, apduBytes, true);
                    var apduResponse = BitConverter.ToString(apduResponseBytes).Replace("-", "");
                    return Ok(apduResponse);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RestController.SendApdu error:{ex}");
                throw;
            }
        }

        public class SendApduRequest
        {
            public required Device device {get; set;}
            public required string apdu {get; set;}
            
        }

        public class Device
        {
            public required int id { get; set; }
            public required DeviceAttributes attributes { get; set; }
        }
        

        public class DeviceAttributes
        {
            public int clientId { get; set; }
        }
    }
}
