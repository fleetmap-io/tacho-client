using PCSC;

namespace TachoClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Start");
            try
            {
                ISCardContext Context = ContextFactory.Instance.Establish(SCardScope.System);
                var readers = Context.GetReaders();
                Console.WriteLine("Found {0} reader(s)!", readers.Length);
                foreach(var reader in readers)
                {
                    Console.WriteLine(reader);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            Console.WriteLine("End");
        }
    }
}