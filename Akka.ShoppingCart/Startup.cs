// -----------------------------------------------------------------------
//  <copyright file="Startup.cs" company="Petabridge, LLC">
//      Copyright (C) 2015-2023 Petabridge, LLC <https://petabridge.com>
//      Copyright (c) Microsoft. All rights reserved.
//      Licensed under the MIT License.
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Discovery.Config.Hosting;
using LogLevel = Akka.Event.LogLevel;
using Dns = System.Net.Dns;

namespace Akka.ShoppingCart;

public class Startup
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    
    public Startup(WebHostBuilderContext context)
    {
        _configuration = context.Configuration;
        _environment = context.HostingEnvironment;
    }
    
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMudServices();
        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddHttpContextAccessor();
        services.AddSingleton<ShoppingCartService>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<ProductService>();
        services.AddScoped<ComponentStateChangedObserver>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<ClusterStateBroadcastService>();
        services.AddLocalStorageServices();
        services.AddApplicationInsights("Node");
        
        services.AddAkka("ShoppingCart", builder =>
        {
            builder
                .ConfigureLoggers(logger =>
                {
                    logger.LogLevel = LogLevel.InfoLevel;
                    logger.ClearLoggers();
                    logger.AddLoggerFactory();
                })
                
                // See HostingExtensions.AddShoppingCartRegions() to see how the shard regions are set-up
                .AddShoppingCartRegions()
                .WithActors((system, registry) =>
                {
                    var listener = system.ActorOf(Props.Create(() => new ClusterListenerActor()));
                    registry.TryRegister<RegistryKey.ClusterStateListener>(listener);
                });

            const int remotePort = 12552;
            const int managementPort = 18558;
            if (_environment.IsDevelopment())
            {
                // For local development, we will be using Akka.Discovery.ConfigServiceDiscovery instead of Akka.Discovery.Azure
                // We will also use in memory persistence providers instead of using Akka.Persistence.Azure
                builder
                    .WithAkkaManagement(new AkkaManagementOptions
                    {
                        HostName = "localhost",
                        BindHostName = "localhost",
                        Port = managementPort,
                        BindPort = managementPort
                    })
                    .WithClusterBootstrap(setup =>
                    {
                        setup.ContactPointDiscovery.ServiceName = nameof(ShoppingCartService);
                        setup.ContactPointDiscovery.RequiredContactPointsNr = 1;
                    })
                    .WithConfigDiscovery(options =>
                    {
                        options.Services = new List<Service>
                        {
                            new Service
                            {
                                Name = nameof(ShoppingCartService),
                                Endpoints = new[] { $"localhost:{managementPort}" }
                            }
                        };
                    })
                    .WithRemoting("localhost", remotePort)
                    .WithClustering()
                    .WithInMemoryJournal()
                    .WithInMemorySnapshotStore()
                    .WithActors(async (_, registry) =>
                    {
                        var productRegion = registry.Get<RegistryKey.ProductRegion>();
                        var faker = new ProductDetails().GetBogusFaker();

                        foreach (var product in faker.GenerateLazy(50))
                        {
                            await productRegion.Ask<Done>(new Product.CreateOrUpdate(product));
                        }
                    });
            }
            else
            {
                var connectionString = _configuration["AZURE_STORAGE_CONNECTION_STRING"];
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new Exception("Missing AZURE_STORAGE_CONNECTION_STRING environment variable");
                
                var endpointAddress = GetPublicIpAddress();
                
                builder
                    .WithAkkaManagement(new AkkaManagementOptions
                    {
                        HostName = endpointAddress,
                        BindHostName = endpointAddress,
                        Port = managementPort,
                        BindPort = managementPort
                    })
                    .WithClusterBootstrap(setup =>
                    {
                        setup.ContactPointDiscovery.ServiceName = nameof(ShoppingCartService);
                        setup.ContactPointDiscovery.RequiredContactPointsNr = 1;
                    })
                    .WithAzureDiscovery(setup =>
                    {
                        setup.HostName = endpointAddress;
                        setup.Port = managementPort;
                        setup.ServiceName = nameof(ShoppingCartService);
                        setup.ConnectionString = connectionString;
                    })
                    .WithClustering()
                    .WithRemoting(endpointAddress, remotePort)
                    .WithAzureTableJournal(connectionString)
                    .WithAzureBlobsSnapshotStore(connectionString);
            }
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseForwardedHeaders();
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
        });
    }

    private static string GetPublicIpAddress()
    {
        var hostName = Dns.GetHostName();
        var addresses = Dns.GetHostAddresses(hostName);
        var ip = addresses.FirstOrDefault(ip =>
            ip.AddressFamily == AddressFamily.InterNetwork &&
            !Equals(ip, IPAddress.Any) &&
            !Equals(ip, IPAddress.Loopback));
        if (ip is null)
            throw new Exception($"Could not find a valid public IP address for host name {hostName}");
        
        return ip.ToString();
    }
}