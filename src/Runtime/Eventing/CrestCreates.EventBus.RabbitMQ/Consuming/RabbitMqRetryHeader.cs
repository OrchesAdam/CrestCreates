using System.Globalization;
using System.Text;
using RabbitMQ.Client;

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Framework-owned retry budget stored in the message's persistent headers. Reads tolerate the
/// documented AMQP value shapes (string longstr as UTF-8 bytes, string, integer); negative, overflowing
/// or mistyped values are deterministically rejected instead of being reset to zero (which would allow
/// an unbounded redelivery loop).
/// </summary>
public static class RabbitMqRetryHeader
{
    public const string Key = "x-retry-count";

    public static bool TryRead(IReadOnlyBasicProperties? properties, out int retryCount, out string? problem)
    {
        retryCount = 0;
        problem = null;

        if (properties?.Headers is null ||
            !properties.Headers.TryGetValue(Key, out var value) ||
            value is null)
        {
            return true;
        }

        switch (value)
        {
            case byte[] bytes:
                var text = Encoding.UTF8.GetString(bytes);
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out retryCount))
                {
                    return Validate(retryCount, ref problem);
                }

                problem = $"retry header '{Key}' is not an integer (raw value: '{text}')";
                return false;

            case string textValue:
                if (int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out retryCount))
                {
                    return Validate(retryCount, ref problem);
                }

                problem = $"retry header '{Key}' is not an integer (raw value: '{textValue}')";
                return false;

            case sbyte or byte or short or ushort or int or uint or long:
                try
                {
                    retryCount = checked((int)Convert.ToInt64(value, CultureInfo.InvariantCulture));
                }
                catch (OverflowException)
                {
                    problem = $"retry header '{Key}' overflows Int32 (raw value: '{value}')";
                    return false;
                }

                return Validate(retryCount, ref problem);

            default:
                problem = $"retry header '{Key}' has an unsupported type ({value.GetType().Name}); " +
                          "expected string or integer";
                return false;
        }
    }

    private static bool Validate(int retryCount, ref string? problem)
    {
        if (retryCount < 0)
        {
            problem = $"retry header '{Key}' is negative ({retryCount})";
            return false;
        }

        return true;
    }
}
