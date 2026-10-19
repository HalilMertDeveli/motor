using System;
using System.Collections.Generic;
using NUnit.Framework;
using Smartstore.Data;

namespace Smartstore.Tests.Data;

[TestFixture]
public class DataSettingsEnvironmentTests
{
    private const string FileConnection = "Host=localhost;Database=motor;Username=motor;Password=file-secret";
    private const string EnvConnection = "Host=db.example.test;Database=postgres;Username=motor_app;Password=env-secret;SSL Mode=VerifyFull";

    private Func<string, string> _originalReader;
    private Dictionary<string, string> _env;

    [SetUp]
    public void SetUp()
    {
        _originalReader = DataSettings.EnvironmentReader;
        _env = new Dictionary<string, string>();
        DataSettings.EnvironmentReader = name => _env.TryGetValue(name, out var value) ? value : null;
    }

    [TearDown]
    public void TearDown()
    {
        DataSettings.EnvironmentReader = _originalReader;
    }

    [Test]
    public void Environment_connection_string_is_null_when_unset_or_blank()
    {
        Assert.That(DataSettings.EnvironmentConnectionString, Is.Null);
        Assert.That(DataSettings.UsesEnvironmentConnectionString, Is.False);

        _env[DataSettings.ConnectionStringEnvVar] = "   ";
        Assert.That(DataSettings.EnvironmentConnectionString, Is.Null);
        Assert.That(DataSettings.UsesEnvironmentConnectionString, Is.False);
    }

    [Test]
    public void Environment_values_are_trimmed()
    {
        _env[DataSettings.ConnectionStringEnvVar] = "  " + EnvConnection + "\n";
        _env[DataSettings.DataProviderEnvVar] = " postgresql ";

        Assert.That(DataSettings.EnvironmentConnectionString, Is.EqualTo(EnvConnection));
        Assert.That(DataSettings.EnvironmentDataProvider, Is.EqualTo("postgresql"));
        Assert.That(DataSettings.UsesEnvironmentConnectionString, Is.True);
    }

    [Test]
    public void Environment_wins_over_file()
    {
        Assert.That(DataSettings.ResolveConnectionString(FileConnection, EnvConnection), Is.EqualTo(EnvConnection));
        Assert.That(DataSettings.ResolveConnectionString(DataSettings.EnvironmentPlaceholder, EnvConnection), Is.EqualTo(EnvConnection));
    }

    [Test]
    public void File_is_used_without_environment()
    {
        Assert.That(DataSettings.ResolveConnectionString(FileConnection, null), Is.EqualTo(FileConnection));
    }

    [Test]
    public void Placeholder_without_environment_fails_closed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DataSettings.ResolveConnectionString(DataSettings.EnvironmentPlaceholder, null));
        Assert.That(ex.Message, Does.Contain(DataSettings.ConnectionStringEnvVar));
        Assert.That(ex.Message, Does.Not.Contain("secret"));
    }

    [Test]
    public void Secret_is_never_persisted_while_environment_is_used()
    {
        Assert.That(DataSettings.GetPersistableConnectionString(EnvConnection, EnvConnection), Is.EqualTo(DataSettings.EnvironmentPlaceholder));
        Assert.That(DataSettings.GetPersistableConnectionString(FileConnection, null), Is.EqualTo(FileConnection));
    }

    [Test]
    public void Provider_mismatch_is_rejected()
    {
        Assert.DoesNotThrow(() => DataSettings.EnsureProviderMatches("PostgreSql", "postgresql"));
        Assert.DoesNotThrow(() => DataSettings.EnsureProviderMatches("PostgreSql", null));
        Assert.Throws<InvalidOperationException>(() => DataSettings.EnsureProviderMatches("SqlServer", "postgresql"));
    }
}
