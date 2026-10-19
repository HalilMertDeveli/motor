using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MotorShop.Fitment.Domain;
using NUnit.Framework;
using Smartstore;
using Smartstore.Core.Data;
using Smartstore.Data;
using Smartstore.Data.Providers;
using Smartstore.Engine;
using Smartstore.Test.Common;
using Smartstore.Utilities;

namespace MotorShop.CatalogImport.Tests;

/// <summary>
/// Same engine and in-memory <see cref="SmartDbContext"/> bootstrap as Smartstore.Core.Tests' ServiceTestBase,
/// with the Fitment and CatalogImport module assemblies added to the model.
/// </summary>
[TestFixture]
public abstract class CatalogTestBase
{
    private SmartDbContext _dbContext;
    private IDisposable _lifetimeToken;

    protected SmartDbContext DbContext
        => _dbContext;

    [OneTimeSetUp]
    public void SetUp()
    {
        CommonHelper.IsHosted = false;
        CommonHelper.IsDevEnvironment = false;

        var host = CreateHostBuilder().Build();

        var appContext = host.Services.GetRequiredService<IApplicationContext>();
        var providerContainer = (appContext as IServiceProviderContainer)
            ?? throw new ApplicationException($"The implementation of '${nameof(IApplicationContext)}' must also implement '${nameof(IServiceProviderContainer)}'.");
        providerContainer.ApplicationServices = host.Services;

        var engine = host.Services.GetRequiredService<IEngine>();
        var scopeAccessor = host.Services.GetRequiredService<ILifetimeScopeAccessor>();
        engine.Scope = new ScopedServiceContainer(
            scopeAccessor,
            host.Services.GetRequiredService<IHttpContextAccessor>(),
            host.Services.AsLifetimeScope());

        InitDbContext();

        _lifetimeToken = scopeAccessor.BeginContextAwareScope(out _);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _lifetimeToken.Dispose();
        _dbContext.Dispose();

        // INFO: required for IApplicationContext.TenantRoot to be not null after subsequent setups.
        DataSettings.Reload();
    }

    private static IHostBuilder CreateHostBuilder()
    {
        IEngineStarter starter = null;

        return Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureAppConfiguration((context, builder) =>
            {
                builder.AddEnvironmentVariables();
            })
            .ConfigureHostOptions((context, options) =>
            {
                context.HostingEnvironment.EnvironmentName = Debugger.IsAttached ? Environments.Development : Environments.Production;
            })
            .ConfigureServices((context, services) =>
            {
                var appContext = new SmartApplicationContext(
                    context.HostingEnvironment,
                    context.Configuration,
                    NullLogger.Instance);

                appContext.AppConfiguration.EngineType = typeof(TestEngine).AssemblyQualifiedName;

                starter = EngineFactory
                    .Create(appContext.AppConfiguration)
                    .Start(appContext);

                starter.ConfigureServices(services);
            })
            .ConfigureContainer<ContainerBuilder>((context, builder) =>
            {
                starter.ConfigureContainer(builder);
                starter.Dispose();
            });
    }

    private void InitDbContext()
    {
        var dataSettings = new DataSettings
        {
            AppVersion = SmartstoreVersion.Version,
            ConnectionString = "Test",
            TenantName = "Default",
            TenantRoot = null,
            DbFactory = new TestDbFactory()
        };

        DataSettings.Instance = dataSettings;
        DataSettings.SetTestMode(true);

        var builder = new DbContextOptionsBuilder<SmartDbContext>()
            .UseDbFactory(factoryBuilder =>
            {
                factoryBuilder.AddModelAssemblies(new[]
                {
                    typeof(SmartDbContext).Assembly,
                    // What the module's Startup adds via IDbContextConfigurationSource.
                    typeof(MotorVehicle).Assembly,
                    typeof(MotorShop.CatalogImport.Domain.CatalogPart).Assembly
                });
            });

        _dbContext = new SmartDbContext((DbContextOptions<SmartDbContext>)builder.Options);
        _dbContext.Database.EnsureCreated();
    }

    private class TestEngine : IEngine
    {
        public IApplicationContext Application { get; set; }
        public ScopedServiceContainer Scope { get; set; }
        public bool IsStarted { get; set; }
        public bool IsInitialized { get; set; }

        public IEngineStarter Start(IApplicationContext application)
        {
            Guard.NotNull(application);

            Application = application;

            return new EngineStarter(this);
        }

        private class EngineStarter : EngineStarter<TestEngine>
        {
            public EngineStarter(TestEngine engine)
                : base(engine)
            {
            }

            protected override IEnumerable<Assembly> ResolveCoreAssemblies()
                => new[] { typeof(IEngine).Assembly };
        }
    }
}
