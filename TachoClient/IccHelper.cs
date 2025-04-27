using PCSC;
using System.Text.Json;

namespace TachoClient
{
    public static class IccHelper
    {
        private static object IccReaderDicMutex = new object();
        private static Dictionary<string, string> IccReaderDic = new Dictionary<string, string>();
        private static Dictionary<string, string> ReaderIccDic = new Dictionary<string, string>();

        private static object CompanyIccListMutex = new object();
        private static Dictionary<int, List<string>> CompanyIccList = new Dictionary<int, List<string>>();

        private static object IccLockMutex = new object();
        private static Dictionary<string, DateTime> IccLock = new Dictionary<string, DateTime>();

        private static object CardReaderCacheMutex = new object();
        private static Dictionary<int, ICardReader> CardReaderCache = new Dictionary<int, ICardReader>();

        public static string? GetReaderName(string icc)
        {
            lock (IccReaderDicMutex)
            {
                return IccReaderDic.TryGetValue(icc, out var result) ? result : null;
            }
        }
        public static string? GetIcc(string readerName)
        {
            lock (IccReaderDicMutex)
            {
                return ReaderIccDic.TryGetValue(readerName, out var result) ? result : null;
            }
        }

        public static string? GetIcc(int companyId)
        {
            lock (CompanyIccListMutex)
            {
                return CompanyIccList.TryGetValue(companyId, out var list)
                     ? list.FirstOrDefault()
                     : null;                    
            }
        }

        public static ICardReader? GetCardReader(int deviceId)
        {
            lock (CardReaderCacheMutex)
            {
                return CardReaderCache.TryGetValue(deviceId, out var cardReader) ? cardReader : null;
            }
        }
        public static void SetCardReader(int deviceId, ICardReader cardReader)
        {
            lock (CardReaderCacheMutex)
            {
                CardReaderCache[deviceId] = cardReader;
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
                        ReaderIccDic[ri.Name] = ri.Icc;
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

        public static bool LockIcc(string icc)
        {
            lock (IccLockMutex)
            {
                if(IccLock.TryGetValue(icc, out var time))
                {
                    if(time > DateTime.UtcNow)
                    {
                        return false;
                    }
                }

                IccLock[icc] = DateTime.UtcNow.AddMinutes(5);
                return true;
            }
        }

        public static void Unlock(string icc)
        {
            lock (IccLockMutex)
            {
                IccLock.Remove(icc);
            }
        }
        
        public static List<(int companyId, string icc)> GetAllIccsWithCompanyId()
        {
                var list = new List<(int companyId, string icc)>();
                foreach (var kvp in CompanyIccList)
                {
                    var companyId = kvp.Key;
                    var iccs = kvp.Value;
                    foreach (var icc in iccs)
                    {
                        list.Add((companyId, icc));
                    }
                }
                return list;
        }
    }
}
