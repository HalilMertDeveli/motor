using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Smartstore;
using Smartstore.Engine;
using Smartstore.Utilities;

namespace MotorShop.AdminPanel.Tests;

/// <summary>
/// Starts the Smartstore engine once for the test assembly, as Smartstore.Core.Tests' ServiceTestBase does.
/// The DbContext options need it (<c>DbFactoryOptionsExtension</c> reads the app configuration).
/// </summary>
[SetUpFixture]
public class EngineSetup
{
    private IDisposable _lifetimeToken;

    [OneTimeSetUp]
    public void StartEngine()
    {
        CommonHelper.IsHosted = false;
        CommonHelper.IsDevEnvironment = false;

        IEngineStarter starter = null;
        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureAppConfiguration((context, builder) => builder.AddEnvironmentVariables())
            .ConfigureHostOptions((context, options) =>
            {
                context.HostingEnvironment.EnvironmentName = Debugger.IsAttached ? Environments.Development : Environments.Production;
            })
            .ConfigureServices((context, services) =>
            {
                var appContext = new SmartApplicationContext(context.HostingEnvironment, context.Configuration, NullLogger.Instance);
                appContext.AppConfiguration.EngineType = typeof(TestEngine).AssemblyQualifiedName;

                starter = EngineFactory.Create(appContext.AppConfiguration).Start(appContext);
                starter.ConfigureServices(services);
            })
            .ConfigureContainer<ContainerBuilder>((context, builder) =>
            {
                starter.ConfigureContainer(builder);
                starter.Dispose();
            })
            .Build();

        var appContext = host.Services.GetRequiredService<IApplicationContext>();
        ((IServiceProviderContainer)appContext).ApplicationServices = host.Services;

        var engine = host.Services.GetRequiredService<IEngine>();
        var scopeAccessor = host.Services.GetRequiredService<ILifetimeScopeAccessor>();
        engine.Scope = new ScopedServiceContainer(scopeAccessor, host.Services.GetRequiredService<IHttpContextAccessor>(), host.Services.AsLifetimeScope());

        _lifetimeToken = scopeAccessor.BeginContextAwareScope(out _);
    }

    [OneTimeTearDown]
    public void StopEngine()
        => _lifetimeToken?.Dispose();

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

        private class EngineStarter(TestEngine engine) : EngineStarter<TestEngine>(engine)
        {
            protected override IEnumerable<Assembly> ResolveCoreAssemblies()
                => [typeof(IEngine).Assembly];
        }
    }
}
