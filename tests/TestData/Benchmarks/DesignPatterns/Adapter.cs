using System;

namespace DesignPatterns
{
    public interface IMetricDistance { double GetKilometers(); }
    public interface IImperialDistance { double GetMiles(); }

    public class MetricDistance : IMetricDistance
    {
        public double Kilometers { get; }
        public MetricDistance(double km) { Kilometers = km; }
        public double GetKilometers() => Kilometers;
    }

    public class ImperialToMetricAdapter : IMetricDistance
    {
        private IImperialDistance imperial;
        public ImperialToMetricAdapter(IImperialDistance imperial) { this.imperial = imperial; }
        public double GetKilometers() => imperial.GetMiles() * 1.60934;
    }

    public class ImperialDistance : IImperialDistance
    {
        public double Miles { get; }
        public ImperialDistance(double miles) { Miles = miles; }
        public double GetMiles() => Miles;
    }
}
