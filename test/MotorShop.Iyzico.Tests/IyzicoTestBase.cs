using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Domain;
using MotorShop.Iyzico.Services;
using NUnit.Framework;
using Smartstore;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Data;
using Smartstore.Data.Providers;
using Smartstore.Engine;
using Smartstore.Test.Common;
using Smartstore.Threading;
using Smartstore.Utilities;

namespace MotorShop.Iyzico.Tests;

/// <summary>
/// Same engine and in-memory <see cref="SmartDbContext"/> bootstrap as the Fitment tests. Every simulated request gets
/// its own <see cref="SmartDbContext"/> on the shared in-memory database, like separate HTTP requests in production.
/// </summary>
[TestFixture]
public abstract class IyzicoTestBase
{
    public const string ApiKey = "test-api-key";
    public const string SecretKey = "test-secret-key";

    private IDisposable _lifetimeToken;
    private DbContextOptions<SmartDbContext> _options;
    private readonly List<SmartDbContext> _contexts = [];

    protected FakeIyzicoApi Api { get; private set; }
    protected IyzicoSettings Settings { get; private set; }
    protected TestLockProvider LockProvider { get; private set; }
    protected int MarkPaidCalls;
    protected int MarkAuthorizedCalls;
    protected int VoidOfflineCalls;

    /// <summary>
    /// Number of upcoming MarkOrderAsPaidAsync calls that throw, to simulate a database failure.
    /// </summary>
    protected int FailNextMarkPaid;

    protected DateTime Now { get; set; } = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);

    [OneTimeSetUp]
    public void OneTimeSetUp()
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

        DataSettings.Instance = new DataSettings
        {
            AppVersion = SmartstoreVersion.Version,
            ConnectionString = "Test",
            TenantName = "Default",
            TenantRoot = null,
            DbFactory = new TestDbFactory()
        };
        DataSettings.SetTestMode(true);

        _options = (DbContextOptions<SmartDbContext>)new DbContextOptionsBuilder<SmartDbContext>()
            .UseDbFactory(factoryBuilder =>
            {
                factoryBuilder.AddModelAssemblies(new[]
                {
                    typeof(SmartDbContext).Assembly,
                    // What the module's Startup adds via IDbContextConfigurationSource.
                    typeof(IyzicoTransaction).Assembly
                });
            })
            .Options;

        using (var db = new SmartDbContext(_options))
        {
            db.Database.EnsureCreated();
        }

        _lifetimeToken = scopeAccessor.BeginContextAwareScope(out _);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _lifetimeToken.Dispose();
        DataSettings.Reload();
    }

    [SetUp]
    public async Task BaseSetUp()
    {
        Api = new FakeIyzicoApi(SecretKey);
        Settings = new IyzicoSettings();
        LockProvider = new TestLockProvider();
        MarkPaidCalls = 0;
        MarkAuthorizedCalls = 0;
        VoidOfflineCalls = 0;
        FailNextMarkPaid = 0;

        await using var db = NewDbContext();
        db.Set<IyzicoTransaction>().RemoveRange(db.Set<IyzicoTransaction>());
        db.OrderNotes.RemoveRange(db.OrderNotes);
        db.Orders.RemoveRange(db.Orders);
        await db.SaveChangesAsync();
    }

    [TearDown]
    public void BaseTearDown()
    {
        foreach (var db in _contexts)
        {
            db.Dispose();
        }
        _contexts.Clear();
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

                appContext.AppConfiguration.EngineType = typeof(IyzicoTestEngine).AssemblyQualifiedName;

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

    protected SmartDbContext NewDbContext()
        => new(_options);

    /// <summary>
    /// A payment service as resolved for one HTTP request: own DbContext, shared lock provider, API fake and counters.
    /// </summary>
    protected IyzicoPaymentService NewService(IyzicoCredentials credentials = null)
    {
        var db = NewDbContext();
        _contexts.Add(db);

        var credentialProvider = new Mock<IIyzicoCredentialProvider>();
        credentialProvider
            .Setup(x => x.GetCredentialsAsync(It.IsAny<int>()))
            .ReturnsAsync(credentials ?? new IyzicoCredentials(ApiKey, SecretKey, IyzicoCredentialProvider.SandboxBaseUrl));

        var settingFactory = new Mock<ISettingFactory>();
        settingFactory.Setup(x => x.LoadSettingsAsync<IyzicoSettings>(It.IsAny<int>())).ReturnsAsync(() => Settings);

        var orderProcessing = new Mock<IOrderProcessingService>();
        orderProcessing
            .Setup(x => x.MarkOrderAsPaidAsync(It.IsAny<Order>()))
            .Returns<Order>(async order =>
            {
                if (FailNextMarkPaid > 0)
                {
                    FailNextMarkPaid--;
                    throw new DbUpdateException("Simulated database failure.");
                }

                Interlocked.Increment(ref MarkPaidCalls);
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidDateUtc = Now;
                await db.SaveChangesAsync();
            });
        orderProcessing
            .Setup(x => x.MarkAsAuthorizedAsync(It.IsAny<Order>()))
            .Returns<Order>(async order =>
            {
                Interlocked.Increment(ref MarkAuthorizedCalls);
                order.PaymentStatus = PaymentStatus.Authorized;
                await db.SaveChangesAsync();
            });
        orderProcessing
            .Setup(x => x.VoidOfflineAsync(It.IsAny<Order>()))
            .Returns<Order>(async order =>
            {
                Interlocked.Increment(ref VoidOfflineCalls);
                order.PaymentStatus = PaymentStatus.Voided;
                await db.SaveChangesAsync();
            });

        return new IyzicoPaymentService(db, Api, credentialProvider.Object, settingFactory.Object, orderProcessing.Object, LockProvider)
        {
            Logger = NullLogger.Instance,
            UtcNow = () => Now
        };
    }

    protected async Task<Order> CreateOrderAsync(decimal total = 150m, string currency = "TRY", Action<Order> configure = null)
    {
        await using var db = NewDbContext();

        var order = new Order
        {
            OrderGuid = Guid.NewGuid(),
            StoreId = 1,
            CustomerId = 7,
            OrderStatus = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethodSystemName = Providers.IyzicoProvider.SystemName,
            OrderTotal = total,
            CurrencyRate = 1m,
            CustomerCurrencyCode = currency,
            CustomerIp = "203.0.113.10",
            CreatedOnUtc = Now,
            UpdatedOnUtc = Now
        };

        configure?.Invoke(order);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order;
    }

    /// <summary>
    /// Stores an initialized transaction as if the customer had just been redirected to iyzico.
    /// </summary>
    protected async Task<IyzicoTransaction> CreateTransactionAsync(Order order, string token = null, IyzicoTransactionStatus status = IyzicoTransactionStatus.Initialized)
    {
        await using var db = NewDbContext();

        var tx = new IyzicoTransaction
        {
            OrderId = order.Id,
            StoreId = order.StoreId,
            Token = token ?? "tok-" + Guid.NewGuid().ToString("N"),
            ConversationId = Guid.NewGuid().ToString("N"),
            BasketId = order.OrderGuid.ToString(),
            Status = status,
            Amount = IyzicoRequestBuilder.GetOrderAmount(order),
            Currency = order.CustomerCurrencyCode,
            CreatedOnUtc = Now,
            UpdatedOnUtc = Now
        };

        db.Set<IyzicoTransaction>().Add(tx);
        await db.SaveChangesAsync();

        return tx;
    }

    protected async Task<(Order Order, IyzicoTransaction Tx, List<OrderNote> Notes)> LoadAsync(int orderId, string token)
    {
        await using var db = NewDbContext();

        var order = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == orderId);
        var tx = await db.Set<IyzicoTransaction>().AsNoTracking().FirstAsync(x => x.Token == token);
        var notes = await db.OrderNotes.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync();

        return (order, tx, notes);
    }
}

/// <summary>
/// In-process implementation of <see cref="IDistributedLockProvider"/> on top of Smartstore's keyed <see cref="AsyncLock"/>.
/// </summary>
public class TestLockProvider : IDistributedLockProvider
{
    public IDistributedLock GetLock(string key)
        => new TestLock(key);

    private class TestLock(string key) : IDistributedLock
    {
        public string Key => key;

        public bool IsHeld() => AsyncLock.IsLockHeld(key);

        public Task<bool> IsHeldAsync() => Task.FromResult(IsHeld());

        public ILockHandle Acquire(TimeSpan timeout, CancellationToken cancelToken = default)
            => AsyncLock.Keyed(key, timeout, cancelToken);

        public Task<ILockHandle> AcquireAsync(TimeSpan timeout, CancellationToken cancelToken = default)
            => AsyncLock.KeyedAsync(key, timeout, cancelToken);
    }
}

internal class IyzicoTestEngine : IEngine
{
    public IApplicationContext Application { get; set; }
    public ScopedServiceContainer Scope { get; set; }
    public bool IsStarted { get; set; }
    public bool IsInitialized { get; set; }

    public IEngineStarter Start(IApplicationContext application)
    {
        Application = application;
        return new Starter(this);
    }

    private class Starter : EngineStarter<IyzicoTestEngine>
    {
        public Starter(IyzicoTestEngine engine)
            : base(engine)
        {
        }

        protected override IEnumerable<Assembly> ResolveCoreAssemblies()
            => new[] { typeof(IEngine).Assembly };
    }
}
