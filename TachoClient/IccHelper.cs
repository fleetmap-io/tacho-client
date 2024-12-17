using System.Text.Json;

namespace TachoClient
{
    public static class IccHelper
    {
        private static object IccReaderDicMutex = new object();
        private static Dictionary<string, string> IccReaderDic = new Dictionary<string, string>();

        private static object CompanyIccListMutex = new object();
        private static Dictionary<int, List<string>> CompanyIccList = new Dictionary<int, List<string>>();

        public static string GetReaderName(string icc)
        {
            lock (IccReaderDicMutex)
            {
                return IccReaderDic[icc];
            }
        }

        public static string GetIcc(int companyId)
        {
            lock (CompanyIccListMutex)
            {
                return CompanyIccList[companyId].First();
            }
        }

        public static void Update(List<Program.ReaderInfo> readersInfo)
        {
            UpdateIccReaderDic(readersInfo);
            UpdateCompanyIccDic();
        }

        private static void UpdateIccReaderDic(List<Program.ReaderInfo> readersInfo)
        {
            lock (IccReaderDicMutex)
            {
                IccReaderDic = new Dictionary<string, string>();
                foreach (var ri in readersInfo)
                {
                    if (ri.HasCard)
                    {
                        IccReaderDic[ri.Icc] = ri.Name;
                    }
                }
            }
        }
        private static void UpdateCompanyIccDic()
        {
            var r = new HttpClient().GetAsync("https://api.pinme.io/alblambda/tacho/gettachocompanyicclist")
                        .GetAwaiter().GetResult()
                        .Content.ReadAsStringAsync().GetAwaiter().GetResult();

            lock (CompanyIccListMutex)
            {
                CompanyIccList = JsonSerializer.Deserialize<Dictionary<int, List<string>>>(r);
            }
        }
    }
}
