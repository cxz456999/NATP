using System;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NetCoreServer;


namespace NATP.Signaling.Server
{
    class Program
    { // 
        static void Main(string[] args)
        {
            // SSL server port
            int port = 1122;
            if (args.Length > 0)
                port = int.Parse(args[0]);

            Console.WriteLine($"SSL server port: {port}");

            Console.WriteLine();

            // Create and prepare a new SSL server context
            //var context = new SslContext(SslProtocols.Tls12, new X509Certificate2("./natp.pfx", "natp"));

            // Create a new SSL server
            //var server = new NATP_SSL_SignalingServer(context, IPAddress.Any, port);
            // Create a new TCP server
            var server = new NATP_TCP_SignalingServer(IPAddress.Any, port);
            // Start the server
            Console.Write("Server starting...");
            server.Start();
            Console.WriteLine("Done!");

            Console.WriteLine("Press Enter to stop the server or '!' to restart the server...");

            // Perform text input
            for (; ; )
            {
                string line = Console.ReadLine();
                if (string.IsNullOrEmpty(line))
                    break;

                // Restart the server
                if (line == "!")
                {
                    Console.Write("Server restarting...");
                    server.Restart();
                    Console.WriteLine("Done!");
                    continue;
                }
            }

            // Stop the server
            Console.Write("Server stopping...");
            server.Stop();
            Console.WriteLine("Done!");
        }

    }
}