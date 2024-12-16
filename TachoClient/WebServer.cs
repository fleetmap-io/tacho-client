using System.Net;
using System.Text;
namespace TachoClient
{
    class WebServer
    {

        public static async Task Run()
        {
            // Define the prefixes (URLs) that the server will listen to
            string[] prefixes = { "http://localhost:8080/" };

            using (HttpListener listener = new HttpListener())
            {
                foreach (string prefix in prefixes)
                {
                    listener.Prefixes.Add(prefix);
                }

                // Start the listener
                listener.Start();
                Console.WriteLine("HTTP Server started. Listening on: " + string.Join(", ", prefixes));

                try
                {
                    while (true)
                    {
                        var context = await listener.GetContextAsync();
                        // Process the request
                        _ = Task.Run(() => ProcessRequest(context)); // Handle each request in a separate task
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                finally
                {
                    // Stop the listener
                    listener.Stop();
                }
            }
        }

        private static void ProcessRequest(HttpListenerContext context)
        {
            string method = context.Request.HttpMethod;
            string url = context.Request.Url.ToString();
            Console.WriteLine($"Received {method} request for {url}");

            string responseString = "<html><body><h1>Hello, World!</h1></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);

            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
    }
}
