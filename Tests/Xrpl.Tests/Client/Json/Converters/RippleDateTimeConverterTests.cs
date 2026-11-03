using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using XrplTests.Xrpl.Utils;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestURippleDateTimeConverter
{
    private static readonly DateTime RippleEpoch = new(
        year: 2000,
        month: 1,
        day: 1,
        hour: 0,
        minute: 0,
        second: 0,
        DateTimeKind.Utc);

    private class RippleModel
    {
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Timestamp { get; set; }
    }

    [TestMethod]
    public void Deserialize_Integer_ReturnsCorrectDateTime()
    {
        // 784111777 seconds after Ripple Epoch = some date in 2024
        var json = "{\"Timestamp\": 784111777}";
        var result = JsonSerializer.Deserialize<RippleModel>(json, XrplJsonOptions.Default);
        var expected = RippleEpoch.AddSeconds(784111777);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_Zero_ReturnsRippleEpoch()
    {
        var json = "{\"Timestamp\": 0}";
        var result = JsonSerializer.Deserialize<RippleModel>(json, XrplJsonOptions.Default);
        Assert.AreEqual(RippleEpoch, result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_Null_ReturnsNull()
    {
        var json = "{\"Timestamp\": null}";
        var result = JsonSerializer.Deserialize<RippleModel>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_StringNumber_ReturnsCorrectDateTime()
    {
        var json = "{\"Timestamp\": \"784111777\"}";
        var result = JsonSerializer.Deserialize<RippleModel>(json, XrplJsonOptions.Default);
        var expected = RippleEpoch.AddSeconds(784111777);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Serialize_DateTime_ReturnsRippleEpochSeconds()
    {
        var date = RippleEpoch.AddSeconds(784111777);
        var model = new RippleModel
        {
            Timestamp = date,
        };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        RippleModel deserialized = JsonSerializer.Deserialize<RippleModel>(json, XrplJsonOptions.Default);
        Assert.AreEqual(date, deserialized.Timestamp);
    }

    [TestMethod]
    public void Serialize_RippleEpoch_ReturnsZero()
    {
        var model = new RippleModel
        {
            Timestamp = RippleEpoch,
        };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        RippleModel deserialized = JsonSerializer.Deserialize<RippleModel>(json, XrplJsonOptions.Default);
        Assert.AreEqual(RippleEpoch, deserialized.Timestamp);
    }

    [TestMethod]
    public void Serialize_DateTimeBeforeTheEpoch_ReturnsZero()
    {
        RippleModel model = new RippleModel { Timestamp = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc) };

        Assert.AreEqual("{\"Timestamp\":0}", JsonSerializer.Serialize(model, XrplJsonOptions.Default));
    }

    [TestMethod]
    public void Serialize_DateTimeMaxValue_SaturatesToUInt32Max()
    {
        RippleModel model = new RippleModel { Timestamp = DateTime.MaxValue };

        Assert.AreEqual("{\"Timestamp\":4294967295}", JsonSerializer.Serialize(model, XrplJsonOptions.Default));
    }

    [TestMethod]
    public void Serialize_Null_ReturnsJsonNull()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(XrplJsonOptions.Default)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        var model = new RippleModel { Timestamp = null };
        string json = JsonSerializer.Serialize(model, options);
        StringAssert.Contains(json, "\"Timestamp\":null");
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        var original = new DateTime(year: 2024, month: 6, day: 15, hour: 12, minute: 30, second: 0, DateTimeKind.Utc);
        var model = new RippleModel
        {
            Timestamp = original,
        };
        var json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<RippleModel>(json, XrplJsonOptions.Default);
        Assert.AreEqual(original, deserialized.Timestamp);
    }
}

[TestClass]
public class TestUUnixDateTimeConverter
{
    private static readonly DateTime UnixEpoch = new(
        year: 1970,
        month: 1,
        day: 1,
        hour: 0,
        minute: 0,
        second: 0,
        DateTimeKind.Utc);

    private class UnixModel
    {
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTime? Timestamp { get; set; }
    }

    [TestMethod]
    public void Deserialize_Integer_ReturnsCorrectDateTime()
    {
        // 1718451000 = 2024-06-15 12:30:00 UTC
        var json = "{\"Timestamp\": 1718451000}";
        var result = JsonSerializer.Deserialize<UnixModel>(json, XrplJsonOptions.Default);
        var expected = UnixEpoch.AddSeconds(1718451000);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_Zero_ReturnsUnixEpoch()
    {
        var json = "{\"Timestamp\": 0}";
        var result = JsonSerializer.Deserialize<UnixModel>(json, XrplJsonOptions.Default);
        Assert.AreEqual(UnixEpoch, result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_Null_ReturnsNull()
    {
        var json = "{\"Timestamp\": null}";
        var result = JsonSerializer.Deserialize<UnixModel>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_StringNumber_ReturnsCorrectDateTime()
    {
        var json = "{\"Timestamp\": \"1718451000\"}";
        var result = JsonSerializer.Deserialize<UnixModel>(json, XrplJsonOptions.Default);
        var expected = UnixEpoch.AddSeconds(1718451000);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Serialize_DateTime_ReturnsUnixSeconds()
    {
        var date = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        var model = new UnixModel { Timestamp = date };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        UnixModel deserialized = JsonSerializer.Deserialize<UnixModel>(json, XrplJsonOptions.Default);
        Assert.AreEqual(date, deserialized.Timestamp);
    }

    [TestMethod]
    public void Serialize_UnixEpoch_ReturnsZero()
    {
        var model = new UnixModel
        {
            Timestamp = UnixEpoch,
        };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        UnixModel deserialized = JsonSerializer.Deserialize<UnixModel>(json, XrplJsonOptions.Default);
        Assert.AreEqual(UnixEpoch, deserialized.Timestamp);
    }

    [TestMethod]
    public void Serialize_Null_ReturnsJsonNull()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(XrplJsonOptions.Default)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        var model = new UnixModel { Timestamp = null };
        string json = JsonSerializer.Serialize(model, options);
        StringAssert.Contains(json, "\"Timestamp\":null");
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        var original = new DateTime(year: 2024, month: 6, day: 15, hour: 12, minute: 30, second: 0, DateTimeKind.Utc);
        var model = new UnixModel
        {
            Timestamp = original,
        };
        var json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<UnixModel>(json, XrplJsonOptions.Default);
        Assert.AreEqual(original, deserialized.Timestamp);
    }

    [TestMethod]
    public void DifferentFromRippleConverter_SameInput()
    {
        var json = "{\"Timestamp\": 1718451000}";
        var unixResult = JsonSerializer.Deserialize<UnixModel>(json, XrplJsonOptions.Default);
        var rippleJson = json.Replace(oldValue: "Timestamp", newValue: "Timestamp");
        var rippleModel = JsonSerializer.Deserialize<RippleModel>(rippleJson, XrplJsonOptions.Default);

        // Unix и Ripple конвертеры дают разные даты для одного числа
        // Разница = 946684800 секунд (30 лет)
        var diff = (unixResult.Timestamp.Value - rippleModel.Timestamp.Value).TotalSeconds;
        Assert.AreEqual(expected: -946684800, diff);
    }

    private class RippleModel
    {
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Timestamp { get; set; }
    }
}

/// <summary>
/// Writing a <see cref="DateTimeKind.Local"/> value under a local zone other than UTC. Not parallel:
/// the zone is process-wide.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TestURippleDateTimeConverterLocalZone
{
    private class Model
    {
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Timestamp { get; set; }
    }

    [TestMethod]
    public void Serialize_LocalDateTime_IsConvertedToUtcFirst()
    {
        using LocalTimeZoneScope zone = LocalTimeZoneScope.Use(LocalTimeZoneScope.PlusFiveThirty);

        Model model = new Model { Timestamp = new DateTime(2000, 1, 1, 5, 30, 10, DateTimeKind.Local) };

        Assert.AreEqual("{\"Timestamp\":10}", JsonSerializer.Serialize(model, XrplJsonOptions.Default));
    }

    [TestMethod]
    public void Serialize_Now_EqualsUtcNow()
    {
        using LocalTimeZoneScope zone = LocalTimeZoneScope.Use(LocalTimeZoneScope.PlusFiveThirty);

        DateTime utcNow = DateTime.UtcNow;
        DateTime now = utcNow.ToLocalTime();

        string local = JsonSerializer.Serialize(new Model { Timestamp = now }, XrplJsonOptions.Default);
        string utc = JsonSerializer.Serialize(new Model { Timestamp = utcNow }, XrplJsonOptions.Default);

        Assert.AreEqual(utc, local);
    }

    [TestMethod]
    public void Serialize_UnspecifiedDateTime_IsTakenAsUtc()
    {
        using LocalTimeZoneScope zone = LocalTimeZoneScope.Use(LocalTimeZoneScope.PlusFiveThirty);

        Model model = new Model { Timestamp = new DateTime(2000, 1, 1, 0, 0, 10, DateTimeKind.Unspecified) };

        Assert.AreEqual("{\"Timestamp\":10}", JsonSerializer.Serialize(model, XrplJsonOptions.Default));
    }
}
