namespace SyntheticLib;

public enum TemperatureScale
{
    Celsius,
    Fahrenheit,
    Kelvin
}

public class Temperature
{
    public double Value { get; }
    public TemperatureScale Scale { get; }

    public Temperature(double value, TemperatureScale scale)
    {
        Value = value;
        Scale = scale;
    }

    public double ToCelsius()
    {
        if (Scale == TemperatureScale.Celsius)
            return Value;
        if (Scale == TemperatureScale.Fahrenheit)
            return (Value - 32) * 5.0 / 9.0;
        return Value - 273.15;
    }

    public double ToFahrenheit()
    {
        double celsius = ToCelsius();
        return celsius * 9.0 / 5.0 + 32;
    }

    public double ToKelvin()
    {
        double celsius = ToCelsius();
        return celsius + 273.15;
    }

    public bool IsFreezingPoint()
    {
        double celsius = ToCelsius();
        return celsius <= 0;
    }

    public bool IsBoilingPoint()
    {
        double celsius = ToCelsius();
        return celsius >= 100;
    }

    public static string GetDescription(double celsius)
    {
        if (celsius < -40)
            return "Extreme cold";
        if (celsius < 0)
            return "Freezing";
        if (celsius < 10)
            return "Cold";
        if (celsius < 20)
            return "Cool";
        if (celsius < 30)
            return "Warm";
        if (celsius < 40)
            return "Hot";
        return "Extreme heat";
    }
}
