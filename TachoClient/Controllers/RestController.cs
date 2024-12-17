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
        public IActionResult SendApdu(Device device, string apdu)
        {
            try
            {
                var companyId = device.attributes.clientId;
                var icc = IccHelper.GetIcc(companyId);
                var readerName = IccHelper.GetReaderName(icc);

                using (var reader = Program.context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    var apduBytes = Enumerable.Range(0, apdu.Length / 2).Select(x => Convert.ToByte(apdu.Substring(x * 2, 2), 16)).ToArray();
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
