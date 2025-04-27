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
                        var allIccs = IccHelper.GetAllIccsWithCompanyId();
                        var iccListStr = allIccs.Any()
                            ? string.Join("\n", allIccs.Select(x => $"companyId:{x.companyId}, icc:{x.icc}"))
                            : "no ICCs available";

                        return NotFound($"No ICC for companyId:{companyId}.\nAvailable ICCs:\n{iccListStr}");
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
                var apduResponseBytes = Program.SendApduToSmartCard(cardReader, apduBytesFixed);
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

        //-- ino part
        private static object IccSessionDicMutex = new object();
        private static Dictionary<string, string> IccSessionDic = new Dictionary<string, string>();


        [HttpGet("/lock")]
        public IActionResult Lock(string icc)
        {
            try
            {
                if (!IccHelper.LockIcc(icc))
                {
                    return Conflict($"Cannot lock card with icc:{icc}");
                }

                var sessionid = Guid.NewGuid().ToString();
                lock (IccSessionDicMutex)
                {
                    IccSessionDic.Add(sessionid, icc);
                }
                return Ok(sessionid);
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.lock icc:{icc} error:{ex}");
                throw;
            }
        }

        [HttpGet("/getatr")]
        public IActionResult GetAtr(string sessionid)
        {
            try
            {
                string icc;
                lock (IccSessionDicMutex)
                {
                    if(!IccSessionDic.TryGetValue(sessionid, out icc))
                    {
                        return NotFound($"Cannot find icc for sessionid:{sessionid}");
                    }
                }

                var readerName = IccHelper.GetReaderName(icc);
                if (string.IsNullOrEmpty(readerName))
                {
                    return NotFound($"Cannot find reader for icc:{icc}");
                }

                //Reset Card
                string atr;
                using (var cr = Program.context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    byte[] atrValue = cr.GetAttrib(SCardAttribute.AtrString);
                    atr = BitConverter.ToString(atrValue);
                    cr.Disconnect(SCardReaderDisposition.Reset);
                }

                return Ok(atr);
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.getatr sessionid:{sessionid} error:{ex}");
                throw;
            }
        }

        [HttpGet("/apdu")]
        public IActionResult Apdu(string sessionid, string apdu)
        {
            try
            {
                string icc;
                lock (IccSessionDicMutex)
                {
                    if (!IccSessionDic.TryGetValue(sessionid, out icc))
                    {
                        return NotFound($"Cannot find icc for sessionid:{sessionid}");
                    }
                }

                var readerName = IccHelper.GetReaderName(icc);
                if (string.IsNullOrEmpty(readerName))
                {
                    return NotFound($"Cannot find reader for icc:{icc}");
                }

                var cardReader = Program.context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1);

                apdu = apdu.Replace("-", "");
                var apduBytes = Enumerable.Range(0, apdu.Length / 2).Select(x => Convert.ToByte(apdu.Substring(x * 2, 2), 16)).ToArray();
                var apduBytesFixed = Program.msgCorrection(apduBytes);
                var apduResponseBytes = Program.SendApduToSmartCard(cardReader, apduBytesFixed);
                var apduResponse = BitConverter.ToString(apduResponseBytes);
                return Ok(apduResponse);
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.apdu sessionid:{sessionid} apdu:{apdu} error:{ex}");
                throw;
            }
        }

        [HttpGet("/unlock")]
        public IActionResult Unlock(string sessionid)
        {
            try
            {
                string icc;
                lock (IccSessionDicMutex)
                {
                    if (!IccSessionDic.TryGetValue(sessionid, out icc))
                    {
                        return NotFound($"Cannot find icc for sessionid:{sessionid}");
                    }
                    IccSessionDic.Remove(sessionid);
                }

                IccHelper.Unlock(icc);

                return Ok("ok");
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.unlock sessionid:{sessionid} error:{ex}");
                throw;
            }
        }

        //-- generic part
        [HttpGet("/readers")]
        public IActionResult Readers()
        {
            try
            {
                var result = Program.GetReadersInfo(Program.context);
                return Ok(result);
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.Readers error:{ex}");
                throw;
            }
        }

        [HttpGet("/readernames")]
        public IActionResult ReaderNames()
        {
            try
            {
                return Ok(Program.context.GetReaders());
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.ReaderNames error:{ex}");
                throw;
            }
        }

        [HttpGet("/icc")]
        public IActionResult Icc(string readerName)
        {
            try
            {
                string error;
                var icc = Program.GetICC(Program.context, readerName, out error);
                return Ok(string.IsNullOrEmpty(error) ? icc : error);
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.Icc readerName:{readerName} error:{ex}");
                throw;
            }
        }

        [HttpGet("/reset")]
        public IActionResult Reset(string readerName)
        {
            try
            {
                using (var cr = Program.context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    cr.Disconnect(SCardReaderDisposition.Reset);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                Program.Log($"RestController.Reset readerName:{readerName} error:{ex}");
                throw;
            }
        }
    }
}
