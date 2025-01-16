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

                if (req.apduSequenceNumber == "0000")
                {
                    //CompanyId -> ICC
                    var companyId = req.device.attributes.clientId;
                    var icc = IccHelper.GetIcc(companyId);
                    if (string.IsNullOrEmpty(icc))
                    {
                        return NotFound($"No icc for companyId:{companyId}");
                    }

                    //Lock Card
                    if (!IccHelper.LockIcc(icc))
                    {
                        return Conflict($"Cannot lock card with icc:{icc} for deviceId:{deviceId}");
                    }

                    //ICC -> ReaderName
                    var readerName = IccHelper.GetReaderName(icc);
                    if (string.IsNullOrEmpty(readerName))
                    {
                        return NotFound($"No readerName for icc:{icc}");
                    }

                    //Reset Card
                    using(var cr = Program.context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                    {
                        cr.Disconnect(SCardReaderDisposition.Reset);
                    }

                    IccHelper.SetCardReader(deviceId, Program.context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1));
                }

                var cardReader = IccHelper.GetCardReader(deviceId);
                if(null == cardReader)
                {
                    return NotFound("No cardReader for deviceId:{deviceId}");
                }

                var apduBytes = Enumerable.Range(0, req.apdu.Length / 2).Select(x => Convert.ToByte(req.apdu.Substring(x * 2, 2), 16)).ToArray();
                var apduBytesFixed = Program.msgCorrection(apduBytes);
                var apduResponseBytes = Program.SendApduToSmartCard(cardReader, apduBytesFixed, true);
                var apduResponse = BitConverter.ToString(apduResponseBytes).Replace("-", "");
                return Ok(apduResponse);
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.SendApdu error:{ex}");
                throw;
            }
        }



        [HttpPost("/release")]
        public IActionResult Release(ReleaseRequest req)
        {
            try
            {
                var deviceId = req.device.id;
                var companyId = req.device.attributes.clientId;
                var icc = IccHelper.GetIcc(companyId);
                if (!string.IsNullOrEmpty(icc))
                {
                    IccHelper.Unlock(icc);
                }

                var cardReader = IccHelper.GetCardReader(deviceId);
                if(null != cardReader)
                {
                    cardReader.Disconnect(SCardReaderDisposition.Leave);
                }
                return Ok("ok");
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.Release error:{ex}");
                throw;
            }
        }

        public class SendApduRequest
        {
            public required Device device { get; set; }
            public required string apdu { get; set; }
            public required string apduSequenceNumber { get; set; }
        }

        public class ReleaseRequest
        {
            public required Device device { get; set; }
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

        [HttpGet("/version")]
        public IActionResult Version()
        {
            return Ok("1.2");
        }
    }
}
