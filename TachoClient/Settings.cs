namespace TachoClient
{
    public class Settings
    {
        private Settings() { }
        public static Settings Default = new Settings();

        public string ServerIP = "gps.pinme.io";
        public int ServerPort = 9096;
        public int ConnectionFirstTimeOutMinutes = 5;
        public int ConnectionTimeOutMinutes = 5;
    }
}
