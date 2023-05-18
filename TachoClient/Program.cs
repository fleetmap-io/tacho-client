using PCSC;
using System.Net.Sockets;
using System.Net;

namespace TachoClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Start");
            try
            {
                ISCardContext context = ContextFactory.Instance.Establish(SCardScope.System);
                while (true)
                {
                    var readers = context.GetReaders();
                    Console.WriteLine("Found {0} reader(s)!", readers.Length);
                    foreach (var readerName in readers)
                    {
                        Console.WriteLine(readerName);
                        var hasCard = HasCard(context, readerName);
                        Console.WriteLine("HasCard:{0}", hasCard);
                        if (hasCard)
                        {
                            var icc = GetICC(context, readerName);
                            if (!string.IsNullOrEmpty(icc))
                            {
                                trydownload(context, readerName, icc);
                                Console.WriteLine("trying download with ICC:" + icc);
                            }
                        }
                    }
                    Thread.Sleep(30 * 1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            Console.WriteLine("End");
        }

        static bool HasCard(ISCardContext context, string readerName)
        {
            try
            {
                using (var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    var cardAtr = reader.GetAttrib(SCardAttribute.AtrString);
                    Console.WriteLine($"ATR: {BitConverter.ToString(cardAtr)}");
                    return true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("HasCard Error, " + e.Message);
                return false;
            }
        }

        protected static readonly byte[] SelectIccFile = { 0x00, 0xA4, 0x02, 0x0C, 0x02, 0x00, 0x02 };
        protected static readonly byte[] ReadFile = { 0x00, 0xB0, 0x00, 0x00, 0x19 };
        private static string GetICC(ISCardContext context, string readerName)
        {
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
                            return Convert.ToBase64String(r2).TrimEnd('=');
                        }
                    }
                    return "";
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("GetICC Error, " + e.Message);
                return "";
            }
        }

        private static byte[] SendApduToSmartCard(ICardReader cardReader, byte[] apdu, bool logApuds)
        {
            if (logApuds) Console.WriteLine("Transmiting APDU to SmartCard: " + BitConverter.ToString(apdu));
            var receiveBuffer = new byte[256];
            var bytesWritten = cardReader.Transmit(apdu, receiveBuffer);
            var response = new byte[bytesWritten];
            Array.Copy(receiveBuffer, response, bytesWritten);
            if (logApuds) Console.WriteLine("Response from SmartCard: " + (response == null ? "null" : response.ToString()));
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
                Console.WriteLine("Initialized socket successfully");

                var iccBytes = System.Text.Encoding.UTF8.GetBytes('$' + icc);
                streamRW.Write(iccBytes, 0, iccBytes.Length); //Sending ICC
                Console.WriteLine("ICC Sent");

                byte[] dataRW = new byte[socketRW.ReceiveBufferSize];
                streamRW.Read(dataRW, 0, socketRW.ReceiveBufferSize);
                var hasdownload = BitConverter.ToBoolean(dataRW, 0);
                Console.WriteLine("Has download: " + hasdownload);
                if (!hasdownload)
                {
                    Console.WriteLine("END Connection -> No downloads pending");
                    return;
                }

                //should come from the server now
                Console.WriteLine("Waiting contact from TachoServer");
                try
                {
                    streamRW.ReadByte();
                }
                catch (Exception a)
                {
                    Console.WriteLine("ERROR" + a.Message);
                    Console.WriteLine("END Connection -> no contact from TachoServer");
                    return;
                }
                Console.WriteLine("Have contact from TachoServer");
                streamRW.ReadTimeout = Settings.Default.ConnectionTimeOutMinutes * 1000 * 60;

                dataRW = new byte[socketRW.ReceiveBufferSize];

                // Get the ATR of the card
                using (var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.T1))
                {
                    byte[] atrValue = reader.GetAttrib(SCardAttribute.AtrString);

                    streamRW.WriteByte(1); //Sending Msg Type = ATR
                    streamRW.Write(atrValue, 0, atrValue.Length); //Sending ATR
                    Console.WriteLine("Sending atr (1) = " + BitConverter.ToString(atrValue));

                    int msgtype = 0;
                    int nbytes = 0;

                    try
                    {
                        Console.WriteLine("Listening");
                        msgtype = streamRW.ReadByte();
                        if (msgtype == -1) { return; }
                        nbytes = streamRW.Read(dataRW, 0, socketRW.ReceiveBufferSize);
                    }
                    catch
                    {
                        Console.WriteLine("END Connection -> connection timeout");
                        return;
                    }

                    while (nbytes > 0)
                    {
                        byte[] truncatedmsg = new byte[nbytes];
                        Array.Copy(dataRW, 0, truncatedmsg, 0, nbytes);
                        Console.WriteLine("received data:" + BitConverter.ToString(truncatedmsg));

                        switch (msgtype)
                        {
                            case 1: //ATR
                                Console.WriteLine("WARN: ATR Message Type Received out of place. strange, but lets respond. ");
                                streamRW.WriteByte(1); //Sending Msg Type = ATR
                                streamRW.Write(atrValue, 0, atrValue.Length); //Sending ATR
                                Console.WriteLine("Sending atr (1) = " + BitConverter.ToString(atrValue));

                                break;
                            case 2: //APDU
                                Console.WriteLine("Before msgCorrection : " + BitConverter.ToString(truncatedmsg));
                                truncatedmsg = msgCorrection(truncatedmsg);
                                Console.WriteLine("After msgCorrection : " + BitConverter.ToString(truncatedmsg));

                                byte[] response = SendApduToSmartCard(reader, truncatedmsg, true);

                                if (response == null)
                                {
                                    Console.WriteLine("ERROR: Invalid response from card. ");
                                    return;
                                }
                                else
                                {
                                    //send response
                                    streamRW.WriteByte(2); //Sending Msg Type = APDU
                                    streamRW.Write(response, 0, response.Length); //APDU Data
                                    Console.WriteLine("Sent APDU: Data=" + BitConverter.ToString(response));
                                }
                                break;
                        }
                        //Listen Again
                        dataRW = new byte[socketRW.ReceiveBufferSize];
                        try
                        {
                            msgtype = streamRW.ReadByte();
                            if (msgtype == -1) { Console.WriteLine("msgtype = -1 -> Server closed connection"); return; }
                            Console.WriteLine("streamRW.ReadByte()");
                            nbytes = streamRW.Read(dataRW, 0, socketRW.ReceiveBufferSize);
                            Console.WriteLine("streamRW.Read");
                        }
                        catch (Exception er)
                        {
                            Console.WriteLine("END Connection -> connection timeout");
                            Console.WriteLine(er);
                            Console.WriteLine(er.Message);
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("END Connection -> general exception");
                Console.WriteLine(e);
                Console.WriteLine(e.Message);
                return;
            }
            finally
            {
                try
                {
                    if (socketRW.Connected) { socketRW.Close(); }
                    Console.WriteLine("Final END");
                }
                catch (Exception e)
                {
                    Console.WriteLine("error on finally {0}", e.Message);
                }
            }
        }

        private static byte[] msgCorrection(byte[] truncatedmsg)
        {
            if (truncatedmsg[0] == 0x00 && truncatedmsg[1] == 0x88)
            {
                byte expectedparametersize = truncatedmsg[4];
                if (expectedparametersize + 6 != truncatedmsg.Length)
                {
                    byte[] temp = new byte[truncatedmsg.Length + 1];
                    Array.Copy(truncatedmsg, 0, temp, 0, truncatedmsg.Length);
                    temp[temp.Length - 1] = 0x80;
                    Console.WriteLine("msgCorrection: 0088 found. Adding 0x80. new msg = " + BitConverter.ToString(temp));
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
                    Console.WriteLine("msgCorrection: 0CB0 found. Adding 0x00. new msg = " + BitConverter.ToString(temp));
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
                    Console.WriteLine("msgCorrection: 0CD6 found. Adding 0x00. new msg = " + BitConverter.ToString(temp));
                    return temp;
                }
            }
            return truncatedmsg;
        }
    }
}