using PCSC;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;

namespace TachoClient
{
    public class Program
    {
        static void Log(string message)
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

        static string[] SendReadersInfo(ISCardContext context)
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
            try
            {
                using (var socketRW = new TcpClient())
                {
                    socketRW.Connect(Dns.GetHostAddresses(Settings.Default.ServerIP), Settings.Default.ServerPort);
                    var readersInfoBytes = System.Text.Encoding.UTF8.GetBytes('#' + JsonSerializer.Serialize(readersInfo));
                    socketRW.GetStream().Write(readersInfoBytes, 0, readersInfoBytes.Length); //Sending ICC
                    Log("Readers Info Sent to server");
                }
            }
            catch(Exception ex)
            {
                Log($"Failed to send readers info. error:{ex}");
            }

            IccHelper.Update(readersInfo);

            return readers;
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
            Log("Start 1.0");
            try
            {
                context = ContextFactory.Instance.Establish(SCardScope.System);
                SendReadersInfo(context);
                Task.Run(() => LaunchController(args));

                while (true)
                {/*
                    var readers = SendReadersInfo(context);
                    foreach (var readerName in readers)
                    {
                        var hasCard = HasCard(context, readerName, out _);
                        if (hasCard)
                        {
                            var icc = GetICC(context, readerName, out _);
                            if (!string.IsNullOrEmpty(icc))
                            {
                                if (IccHelper.LockIcc(icc, 0))
                                {
                                    try
                                    {
                                        Log($"trying download with ICC:{icc} (reader:{readerName})");
                                        trydownload(context, readerName, icc);
                                    }
                                    finally
                                    {
                                        IccHelper.Unlock(icc);
                                    }
                                }
                                else
                                {
                                    Log($"skip locked ICC:{icc} (reader:{readerName})");
                                }
                            }
                        }
                    }*/
                    Thread.Sleep(30 * 1000);
                }
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
        private static string GetICC(ISCardContext context, string readerName, out string error)
        {
            error = "";
            try
            {
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

        public static void trydownload(ISCardContext context, string readerName, string icc)
        {
            TcpClient socketRW = new TcpClient();
            try
            {
                socketRW = new TcpClient();
                socketRW.Connect(Dns.GetHostAddresses(Settings.Default.ServerIP), Settings.Default.ServerPort);
                NetworkStream streamRW = socketRW.GetStream();
                streamRW.ReadTimeout = Settings.Default.ConnectionFirstTimeOutMinutes * 1000 * 60;
                Log("Initialized socket successfully");

                var iccBytes = System.Text.Encoding.UTF8.GetBytes('$' + icc);
                streamRW.Write(iccBytes, 0, iccBytes.Length); //Sending ICC
                Log("ICC Sent");

                byte[] dataRW = new byte[socketRW.ReceiveBufferSize];
                streamRW.Read(dataRW, 0, socketRW.ReceiveBufferSize);
                var hasdownload = BitConverter.ToBoolean(dataRW, 0);
                Log("Has download: " + hasdownload);
                if (!hasdownload)
                {
                    Log("END Connection -> No downloads pending");
                    return;
                }

                //should come from the server now
                Log("Waiting contact from TachoServer");
                try
                {
                    streamRW.ReadByte();
                }
                catch (Exception a)
                {
                    Log("ERROR" + a.Message);
                    Log("END Connection -> no contact from TachoServer");
                    return;
                }
                Log("Have contact from TachoServer");
                streamRW.ReadTimeout = Settings.Default.ConnectionTimeOutMinutes * 1000 * 60;

                dataRW = new byte[socketRW.ReceiveBufferSize];

                // Get the ATR of the card
                using (var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    byte[] atrValue = reader.GetAttrib(SCardAttribute.AtrString);

                    streamRW.WriteByte(1); //Sending Msg Type = ATR
                    streamRW.Write(atrValue, 0, atrValue.Length); //Sending ATR
                    Log("Sending atr (1) = " + BitConverter.ToString(atrValue));

                    int msgtype = 0;
                    int nbytes = 0;

                    try
                    {
                        Log("Listening");
                        msgtype = streamRW.ReadByte();
                        if (msgtype == -1) { return; }
                        nbytes = streamRW.Read(dataRW, 0, socketRW.ReceiveBufferSize);
                    }
                    catch
                    {
                        Log("END Connection -> connection timeout");
                        return;
                    }

                    while (nbytes > 0)
                    {
                        byte[] truncatedmsg = new byte[nbytes];
                        Array.Copy(dataRW, 0, truncatedmsg, 0, nbytes);
                        Log("received data:" + BitConverter.ToString(truncatedmsg));

                        switch (msgtype)
                        {
                            case 1: //ATR
                                Log("WARN: ATR Message Type Received out of place. strange, but lets respond. ");
                                streamRW.WriteByte(1); //Sending Msg Type = ATR
                                streamRW.Write(atrValue, 0, atrValue.Length); //Sending ATR
                                Log("Sending atr (1) = " + BitConverter.ToString(atrValue));

                                break;
                            case 2: //APDU
                                Log("Before msgCorrection : " + BitConverter.ToString(truncatedmsg));
                                truncatedmsg = msgCorrection(truncatedmsg);
                                Log("After msgCorrection : " + BitConverter.ToString(truncatedmsg));

                                byte[] response = SendApduToSmartCard(reader, truncatedmsg, true);

                                if (response == null)
                                {
                                    Log("ERROR: Invalid response from card. ");
                                    return;
                                }
                                else
                                {
                                    //send response
                                    streamRW.WriteByte(2); //Sending Msg Type = APDU
                                    streamRW.Write(response, 0, response.Length); //APDU Data
                                    Log("Sent APDU: Data=" + BitConverter.ToString(response));
                                }
                                break;
                        }
                        //Listen Again
                        dataRW = new byte[socketRW.ReceiveBufferSize];
                        try
                        {
                            msgtype = streamRW.ReadByte();
                            if (msgtype == -1) { Log("msgtype = -1 -> Server closed connection"); return; }
                            Log("streamRW.ReadByte()");
                            nbytes = streamRW.Read(dataRW, 0, socketRW.ReceiveBufferSize);
                            Log("streamRW.Read");
                        }
                        catch (Exception er)
                        {
                            Log("END Connection -> connection timeout");
                            Log(er);
                            Log(er.Message);
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log("END Connection -> general exception");
                Log(e);
                Log(e.Message);
                return;
            }
            finally
            {
                try
                {
                    if (socketRW.Connected) { socketRW.Close(); }
                    Log("Final END");
                }
                catch (Exception e)
                {
                    Log($"error on finally {e.Message}");
                }
            }
        }

        public static byte[] msgCorrection(byte[] truncatedmsg)
        {
            if (truncatedmsg[0] == 0x00 && truncatedmsg[1] == 0x88)
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