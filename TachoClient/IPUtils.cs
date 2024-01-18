using System.Net;

namespace TMService
{
    public static class IPUtils
    {
        public static string GetTachoServerIp()
        {
            var ip = new HttpClient().GetAsync("https://api.pinme.io/alblambda/ipsolver?service=TachoServer")
                .GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult().Trim();

            return IPAddress.Parse(ip).ToString();
        }
    }
}
