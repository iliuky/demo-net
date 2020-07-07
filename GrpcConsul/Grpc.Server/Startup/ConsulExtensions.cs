using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Grpc.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Grpc.Server.Startup
{
    public static class ConsulExtensions
    {
        public static void AddConsul(this IServiceCollection services)
        {
            services.AddSingleton<ConsulClient>(p =>
            {
                return new ConsulClient(o =>
                {
                    o.Datacenter = "dc-a";
                    o.Address = new Uri("http://127.0.0.1:8500");
                });
            });
        }

        public static void UseConsul(this IApplicationBuilder app)
        {
            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            var serverId = Guid.NewGuid().ToString();
            lifetime.ApplicationStarted.Register(() =>
            {
                var options = new AgentServiceRegistration()
                {
                    ID = serverId,//服务编号保证不重复
                    Name = "Grpc.Server",   //服务的名称
                    Address = "127.0.0.1",  //服务ip地址
                    Port = 5001,            //服务端口
                    Check = new AgentServiceCheck //健康检查
                    {
                        HTTP = "http://localhost:5000/health",   //健康检查地址
                        DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(60), // 心跳异常后多久取消服务注册              
                        Interval = TimeSpan.FromSeconds(10),    //健康检查时间间隔，或者称为心跳间隔（定时检查服务是否健康）
                        Timeout = TimeSpan.FromSeconds(10)      //服务的注册时间
                    }
                };
                var result = app.ApplicationServices.GetRequiredService<ConsulClient>().Agent.ServiceRegister(options);
            });

            lifetime.ApplicationStopping.Register(() =>
            {
                Console.WriteLine("Consul 服务注销");
                app.ApplicationServices.GetRequiredService<ConsulClient>().Agent.ServiceDeregister(serverId).Wait();
                Console.WriteLine("Consul 服务注销完成");
                Thread.Sleep(3000);
            });

            app.Map("/health", _app => _app.UseMiddleware<HealthCheckMiddleware>());
        }

        /// <summary>
        /// 启用所有grpc 服务
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static void MapGrpcServices(this IEndpointRouteBuilder builder)
        {
            var builderType = typeof(GrpcEndpointRouteBuilderExtensions);
            var mapGrpcServiceMethod = builderType.GetMethod("MapGrpcService");

            var types = Assembly.GetAssembly(typeof(ConsulExtensions)).GetTypes()
                .Where(u => u.FullName.StartsWith("Grpc.Server.Services") && u.Name.EndsWith("Service"));

            foreach (var item in types)
            {
                var func = mapGrpcServiceMethod.MakeGenericMethod(item);
                func.Invoke(null, new object[] { builder });
            }
        }
    }
}