using PCSC;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;

namespace TachoClient
{
    public class Program
    {
        public static void Log(string message)
        {
            Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm.ss.fff} {message}");
        }
        static void Log(Exception ex)
        {
            Log(ex.ToString());
        }

        public class ReaderInfo
        {
            public string Name { get; set; }
            public bool HasCard { get; set; }
            public string Icc { get; set; }
            public string Error { get; set; }
        }

        public static List<ReaderInfo> GetReadersInfo(ISCardContext context)
        {
            var readers = context.GetReaders();
            Log($"Found {readers.Length} reader(s)!");
            var readersInfo = new List<ReaderInfo>();
            foreach (var readerName in readers)
            {
                var ri = new ReaderInfo();
                ri.Name = readerName;
                ri.HasCard = HasCard(context, readerName, out var error);
                ri.Error = error;
                ri.Icc = "";
                if (ri.HasCard)
                {
                    ri.Icc = GetICC(context, readerName, out var error2);
                    ri.Error = error2;
                };
                readersInfo.Add(ri);
                Log($"{readerName} - HasCard:{ri.HasCard} - ICC:{ri.Icc} error:{ri.Error}");
            }

            IccHelper.Update(readersInfo);

            return readersInfo;
        }

        static void LaunchController(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls("http://0.0.0.0:8080");
            builder.Services.AddControllers();
            var app = builder.Build();
            app.MapControllers();
            app.Run();
        }

        public static ISCardContext context = null;

        static void Main(string[] args)
        {
            Log("Start 2.0");
            try
            {
                context = ContextFactory.Instance.Establish(SCardScope.System);
                GetReadersInfo(context);                
                LaunchController(args);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
            Log("End");
        }

        static bool HasCard(ISCardContext context, string readerName, out string error)
        {
            error = "";
            try
            {
                using (var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    reader.GetAttrib(SCardAttribute.AtrString);
                    return true;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                Log("HasCard Error, " + error);
                return false;
            }
        }

        protected static readonly byte[] SelectIccFile = { 0x00, 0xA4, 0x02, 0x0C, 0x02, 0x00, 0x02 };
        protected static readonly byte[] ReadFile = { 0x00, 0xB0, 0x00, 0x00, 0x19 };
        public static string GetICC(ISCardContext context, string readerName, out string error)
        {
            var locked = false;
            var icc = "";
            error = "";
            try
            {
                icc = IccHelper.GetIcc(readerName);
                if (!string.IsNullOrEmpty(icc))
                {
                    locked = IccHelper.LockIcc(icc);
                    if (!locked)
                    {
                        return icc;
                    }
                }

                using (var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    var r1 = SendApduToSmartCard(reader, SelectIccFile, false);
                    if (Valid(r1))
                    {
                        var r2 = SendApduToSmartCard(reader, ReadFile, false);
                        if (Valid(r2))
                        {
                            return Convert.ToBase64String(r2.SkipLast(2).ToArray()).TrimEnd('=');
                        }
                    }
                    return "";
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                Log("GetICC Error, " + error);
                return "";
            }
            finally
            {
                if (locked)
                {
                    IccHelper.Unlock(icc);
                }
            }
        }

        public static byte[] SendApduToSmartCard(ICardReader cardReader, byte[] apdu, bool logApuds)
        {
            var receiveBuffer = new byte[256];
            var bytesWritten = cardReader.Transmit(apdu, receiveBuffer);
            var response = new byte[bytesWritten];
            Array.Copy(receiveBuffer, response, bytesWritten);
            if (logApuds) Log($"APDU: {BitConverter.ToString(apdu).Replace("-","")} - {(response == null ? "null" : BitConverter.ToString(response).Replace("-", ""))}");
            return response;
        }

        private static bool Valid(byte[] apduResponse)
        {
            return apduResponse[apduResponse.Length-2] == 0x90 && apduResponse[apduResponse.Length-1] == 0x00;
        }

        public static byte[] msgCorrection(byte[] truncatedmsg)
        {
            if (truncatedmsg[0] == 0x00 && truncatedmsg[1] == 0x86)
            {
                if (truncatedmsg.Length - 5 - truncatedmsg[4] - 1 < 0)
                {
                    byte[] temp = new byte[truncatedmsg.Length + 1];
                    Array.Copy(truncatedmsg, 0, temp, 0, truncatedmsg.Length);
                    temp[temp.Length - 1] = 0x00;
                    Log("msgCorrection: 0086 found. Adding 0x00. new msg = " + BitConverter.ToString(temp));
                    return temp;
                }
            }
            if (truncatedmsg[0] == 0x0C && truncatedmsg[1] == 0xA4)
            {
                if (truncatedmsg.Length - 5 - truncatedmsg[4] - 1 < 0)
                {
                    byte[] temp = new byte[truncatedmsg.Length + 1];
                    Array.Copy(truncatedmsg, 0, temp, 0, truncatedmsg.Length);
                    temp[temp.Length - 1] = 0x00;
                    Log("msgCorrection: 0CA4 found. Adding 0x00. new msg = " + BitConverter.ToString(temp));
                    return temp;
                }
            }
            else if (truncatedmsg[0] == 0x00 && truncatedmsg[1] == 0x88)
            {
                byte expectedparametersize = truncatedmsg[4];
                if (expectedparametersize + 6 != truncatedmsg.Length)
                {
                    byte[] temp = new byte[truncatedmsg.Length + 1];
                    Array.Copy(truncatedmsg, 0, temp, 0, truncatedmsg.Length);
                    temp[temp.Length - 1] = 0x80;
                    Log("msgCorrection: 0088 found. Adding 0x80. new msg = " + BitConverter.ToString(temp));
                    return temp;
                }
            }
            else if (truncatedmsg[0] == 0x0C && truncatedmsg[1] == 0xB0)
            {
                byte expectedparametersize = truncatedmsg[4];
                if (expectedparametersize + 6 != truncatedmsg.Length)
                {
                    byte[] temp = new byte[truncatedmsg.Length + 1];
                    Array.Copy(truncatedmsg, 0, temp, 0, truncatedmsg.Length);
                    temp[temp.Length - 1] = 0x00;
                    Log("msgCorrection: 0CB0 found. Adding 0x00. new msg = " + BitConverter.ToString(temp));
                    return temp;
                }
            }
            else if (truncatedmsg[0] == 0x0C && truncatedmsg[1] == 0xD6)
            {
                byte expectedparametersize = truncatedmsg[4];
                if (expectedparametersize + 6 != truncatedmsg.Length)
                {
                    byte[] temp = new byte[truncatedmsg.Length + 1];
                    Array.Copy(truncatedmsg, 0, temp, 0, truncatedmsg.Length);
                    temp[temp.Length - 1] = 0x00;
                    Log("msgCorrection: 0CD6 found. Adding 0x00. new msg = " + BitConverter.ToString(temp));
                    return temp;
                }
            }
            return truncatedmsg;
        }
    }
}
