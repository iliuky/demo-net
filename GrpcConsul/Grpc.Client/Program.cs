using System;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Grpc.Net.Client;
using Grpc.Server;

namespace Grpc.Client
{
    class Program
    {
        async static Task Main(string[] args)
        {
            var serviceName = "Grpc.Server";
            var consulClient = new ConsulClient(c => c.Address = new Uri("http://localhost:8500"));

            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                SslProtocols = SslProtocols.Tls12,
                ServerCertificateCustomValidationCallback = (x, y, z, m) => true,
            };

            handler.ClientCertificates.Add(new X509Certificate2("user-rsa.pfx", "123456"));
            var httpClient = new HttpClient(handler);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < 1000; i++)
            {
                var services = await consulClient.Catalog.Service(serviceName);
                if (services.Response.Length == 0)
                {
                    throw new Exception($"未发现服务 {serviceName}");
                }

                // 简单的进行负载均衡
                var service = services.Response[i % services.Response.Length];
                var address = $"https://{service.ServiceAddress}:{service.ServicePort}";

                var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpClient = httpClient });
                var client = new Greeter.GreeterClient(channel);

                var result = client.SayHello(new HelloRequest() { Name = "dddd" });
                Console.WriteLine(result.Message);

                Thread.Sleep(500);
            }
            sw.Stop();

            Console.WriteLine("累计耗时:{0}", sw.ElapsedMilliseconds);
            Console.ReadKey();
        }
    }
}
