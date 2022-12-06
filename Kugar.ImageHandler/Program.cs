using Kugar.Core.Configuration;
using Kugar.Storage;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NetVips;

namespace Kugar.ImageHandler
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (ModuleInitializer.VipsInitialized)
            {
                Console.WriteLine($"Inited libvips {NetVips.NetVips.Version(0)}.{NetVips.NetVips.Version(1)}.{NetVips.NetVips.Version(2)}");
            }
            else
            {
                Console.WriteLine(ModuleInitializer.Exception.Message);
            }
             

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddMemoryCache();

            builder.Services.Configure<KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            // If using IIS:
            builder.Services.Configure<IISServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            builder.Services.AddResponseCaching();

            // Add services to the container.
            builder.Services.AddOutputCache(options =>
            {
                options.AddPolicy("imagecache", (policy) => policy.Expire(TimeSpan.FromHours(3)));
                options.UseCaseSensitivePaths = false;
               // options.MaximumBodySize = 1024; 
               
                options.SizeLimit = 1024 * 1024 * 1024;
            });

            builder.Services.AddSingleton<IStorage>(x =>
            {
                 
                //var env = (IHostEnvironment)x.GetService(typeof(IHostEnvironment));
                return new LocalStorage(CustomConfigManager.Default["FolderPath"]);
            }); 

            builder.Services.AddControllers( );

            var app = builder.Build();

            app.UseResponseCaching();
            app.UseOutputCache();
             

            app.MapControllers();

            app.Run();
        }
    }
}